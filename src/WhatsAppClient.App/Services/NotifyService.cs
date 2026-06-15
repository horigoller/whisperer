using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Realtime;
using WhatsAppClient.App.Util;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.App.Services;

/// <summary>
/// A machine-to-machine send request (e.g. from Home Assistant). Provide a recipient phone number
/// and either <see cref="Text"/>, or media via <see cref="MediaUrl"/> or <see cref="MediaBase64"/>.
/// </summary>
public sealed record NotifyRequest(
    string? To,
    string? Text = null,
    string? MediaUrl = null,
    string? MediaBase64 = null,
    string? MediaType = null,   // "image" | "video"
    string? Caption = null,
    string? Filename = null);

public sealed record NotifyResult(string WaId, string MessageId, string? WaMessageId, string Kind);

public sealed class NotifyService
{
    /// <summary>Max decoded media size accepted via base64 (WhatsApp's video ceiling).</summary>
    private const int MaxMediaBytes = 16 * 1024 * 1024;

    private readonly IAppRepository _repo;
    private readonly IWhatsAppMessageService _whatsapp;
    private readonly IWhatsAppMediaService _media;
    private readonly IOutboundMediaStore _mediaStore;
    private readonly IRealtimePublisher _realtime;

    public NotifyService(
        IAppRepository repo,
        IWhatsAppMessageService whatsapp,
        IWhatsAppMediaService media,
        IOutboundMediaStore mediaStore,
        IRealtimePublisher realtime)
    {
        _repo = repo;
        _whatsapp = whatsapp;
        _media = media;
        _mediaStore = mediaStore;
        _realtime = realtime;
    }

    /// <summary>Sends a text or media message to a phone number, persisting it so it shows in the console.</summary>
    public async Task<NotifyResult> SendAsync(NotifyRequest req, string sentBy = "api", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.To)) throw new ArgumentException("'to' (phone number) is required.");
        var phoneE164 = PhoneNumbers.ToE164(req.To);           // throws ArgumentException on garbage
        var waId = PhoneNumbers.ToWaId(phoneE164);

        var hasMedia = !string.IsNullOrWhiteSpace(req.MediaUrl) || !string.IsNullOrWhiteSpace(req.MediaBase64);
        if (!hasMedia && string.IsNullOrWhiteSpace(req.Text))
            throw new ArgumentException("Provide 'text' or media ('mediaUrl' or 'mediaBase64').");

        // Only send to known contacts — add the number in the console first.
        if (await _repo.GetContactAsync(waId, ct) is null) throw new ContactNotFoundException(waId);

        var id = Guid.NewGuid().ToString();
        WhatsAppMessage message;
        string kind, preview;
        string? mediaS3Key = null, mediaUrl = null;

        if (hasMedia)
        {
            var mediaType = NormalizeMediaType(req.MediaType);
            var caption = FirstNonBlank(req.Caption, req.Text);   // explicit caption, else the text
            var (body, s3Key) = await BuildMediaBodyAsync(req, mediaType, caption, ct);
            message = BuildMediaMessage(mediaType, phoneE164, id, body);
            kind = mediaType;
            mediaS3Key = s3Key;                                   // staged base64 media re-served from S3
            mediaUrl = string.IsNullOrWhiteSpace(req.MediaUrl) ? null : req.MediaUrl;  // link media rendered directly
            // Preview mirrors what the recipient sees: the caption, or a media placeholder.
            preview = caption ?? $"[{mediaType}]";
        }
        else
        {
            message = new WhatsAppTextMessage
            {
                To = phoneE164,
                Text = new WhatsAppTextBody { Body = req.Text! },
                BizOpaqueCallbackData = id,
            };
            kind = "text";
            preview = req.Text!;
        }

        var awsId = (await _whatsapp.SendMessageAsync(message, ct)).MessageId;
        await PersistOutboundAsync(id, waId, kind, preview, awsId, sentBy, mediaS3Key, mediaUrl, ct);
        return new NotifyResult(waId, id, awsId, kind);
    }

    private static string? FirstNonBlank(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : null);

    private static string NormalizeMediaType(string? mediaType) =>
        (mediaType ?? "").Trim().ToLowerInvariant() switch
        {
            "image" or "img" or "photo" => "image",
            "video" or "clip" => "video",
            "" => throw new ArgumentException("mediaType is required with media: 'image' or 'video'."),
            _ => throw new ArgumentException("mediaType must be 'image' or 'video'."),
        };

    private async Task<(WhatsAppOutboundMedia Body, string? S3Key)> BuildMediaBodyAsync(
        NotifyRequest req, string mediaType, string? caption, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.MediaUrl))
            return (new WhatsAppOutboundMedia { Link = req.MediaUrl, Caption = caption, Filename = req.Filename }, null);

        byte[] bytes;
        try { bytes = Convert.FromBase64String(req.MediaBase64!); }
        catch (FormatException) { throw new ArgumentException("mediaBase64 is not valid base64."); }
        if (bytes.Length == 0) throw new ArgumentException("mediaBase64 is empty.");
        if (bytes.Length > MaxMediaBytes)
            throw new ArgumentException($"media exceeds the {MaxMediaBytes / (1024 * 1024)} MB limit.");

        var (bucket, key) = await _mediaStore.StageAsync(bytes, mediaType, req.Filename, ct);
        var handle = await _media.UploadFromS3Async(bucket, key, ct);
        return (new WhatsAppOutboundMedia { Id = handle, Caption = caption, Filename = req.Filename }, key);
    }

    private static WhatsAppMessage BuildMediaMessage(string mediaType, string to, string id, WhatsAppOutboundMedia body) =>
        mediaType == "video"
            ? new WhatsAppVideoMessage { To = to, BizOpaqueCallbackData = id, Video = body }
            : new WhatsAppImageMessage { To = to, BizOpaqueCallbackData = id, Image = body };

    private async Task PersistOutboundAsync(
        string id, string waId, string kind, string preview, string? awsId, string sentBy,
        string? mediaS3Key, string? mediaUrl, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("o");
        var msg = new ChatMessage
        {
            WaId = waId,
            Id = id,
            Direction = "out",
            Type = kind,
            Text = preview,
            MediaS3Key = mediaS3Key,
            MediaUrl = mediaUrl,
            Status = "sent",
            WaMessageId = awsId,
            SentBy = sentBy,
            CreatedAt = now,
        };
        await _repo.PutMessageAsync(msg, ct);

        var existingTask = _repo.GetConversationAsync(waId, ct);
        var contactTask = _repo.GetContactAsync(waId, ct);
        await Task.WhenAll(existingTask, contactTask);
        var existing = existingTask.Result;
        await _repo.PutConversationAsync(new Conversation
        {
            WaId = waId,
            Name = existing?.Name ?? contactTask.Result?.Name,
            LastPreview = preview,
            LastActivityAt = now,
            WindowExpiresAt = existing?.WindowExpiresAt,
            Unread = existing?.Unread ?? 0,
        }, ct);

        await _realtime.PublishAsync(new RealtimeEvent("message", waId, Message: msg), ct);
    }
}
