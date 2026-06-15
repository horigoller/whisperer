using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Realtime;
using WhatsAppClient.App.Util;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.App.Services;

/// <summary>
/// A machine-to-machine send request (e.g. from Home Assistant). Provide a recipient phone number
/// and one of: <see cref="Template"/> (delivers outside the 24h window), <see cref="Text"/>, or media
/// via <see cref="MediaUrl"/>/<see cref="MediaBase64"/> (both free-form, in-window only).
/// </summary>
public sealed record NotifyRequest(
    string? To,
    string? Text = null,
    string? MediaUrl = null,
    string? MediaBase64 = null,
    string? MediaType = null,   // "image" | "video"
    string? Caption = null,
    string? Filename = null,
    string? Template = null,    // approved template name → window-proof
    string? LanguageCode = null,
    IReadOnlyList<string>? Params = null);

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

        var hasTemplate = !string.IsNullOrWhiteSpace(req.Template);
        var hasMedia = !string.IsNullOrWhiteSpace(req.MediaUrl) || !string.IsNullOrWhiteSpace(req.MediaBase64);
        if (!hasTemplate && !hasMedia && string.IsNullOrWhiteSpace(req.Text))
            throw new ArgumentException("Provide 'template', 'text', or media ('mediaUrl' or 'mediaBase64').");

        // Only send to known contacts — add the number in the console first.
        if (await _repo.GetContactAsync(waId, ct) is null) throw new ContactNotFoundException(waId);

        var id = Guid.NewGuid().ToString();
        WhatsAppMessage message;
        string kind, preview;
        string? persistType = null;   // overrides the persisted message Type (kind) when set
        string? mediaS3Key = null, mediaUrl = null, templateName = null;

        if (hasTemplate)
        {
            // Templates deliver outside the 24h window (the whole point of this path).
            var bodyParams = req.Params ?? [];
            if (bodyParams.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("template 'params' must all be non-empty.");
            templateName = req.Template;
            kind = "template";

            WhatsAppTemplateParameter? header = null;
            if (hasMedia)
            {
                // Attach media as the template's header (the template must have a matching media header).
                var mt = NormalizeMediaType(req.MediaType);
                var (mid, link, s3Key) = await ResolveMediaAsync(req, mt, ct);
                header = HeaderParam(mt, mid, link);
                mediaS3Key = s3Key;
                mediaUrl = link;
                persistType = mt;     // store as image/video so the console renders the header media
                // No-params fallback uses the media placeholder so the console suppresses it as a caption.
                preview = bodyParams.Count > 0 ? string.Join(" ", bodyParams) : $"[{mt}]";
            }
            else
            {
                preview = bodyParams.Count > 0 ? $"[{req.Template}] {string.Join(" ", bodyParams)}" : $"[template: {req.Template}]";
            }
            message = OutboundMessageFactory.Template(phoneE164, id, req.Template!, req.LanguageCode, bodyParams, header);
        }
        else if (hasMedia)
        {
            var mediaType = NormalizeMediaType(req.MediaType);
            var caption = FirstNonBlank(req.Caption, req.Text);   // explicit caption, else the text
            var (mid, link, s3Key) = await ResolveMediaAsync(req, mediaType, ct);
            var body = new WhatsAppOutboundMedia { Id = mid, Link = link, Caption = caption, Filename = req.Filename };
            message = BuildMediaMessage(mediaType, phoneE164, id, body);
            kind = mediaType;
            mediaS3Key = s3Key;                                   // staged base64 media re-served from S3
            mediaUrl = link;                                      // link media rendered directly
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
        await PersistOutboundAsync(id, waId, persistType ?? kind, preview, awsId, sentBy, mediaS3Key, mediaUrl, templateName, ct);
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

    /// <summary>Resolves the request's media to a WhatsApp reference: a public link, or a handle from
    /// staging the base64 bytes in S3 and uploading them. Returns the staged S3 key for re-serving.</summary>
    private async Task<(string? Id, string? Link, string? S3Key)> ResolveMediaAsync(
        NotifyRequest req, string mediaType, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.MediaUrl)) return (null, req.MediaUrl, null);

        byte[] bytes;
        try { bytes = Convert.FromBase64String(req.MediaBase64!); }
        catch (FormatException) { throw new ArgumentException("mediaBase64 is not valid base64."); }
        if (bytes.Length == 0) throw new ArgumentException("mediaBase64 is empty.");
        if (bytes.Length > MaxMediaBytes)
            throw new ArgumentException($"media exceeds the {MaxMediaBytes / (1024 * 1024)} MB limit.");

        var (bucket, key) = await _mediaStore.StageAsync(bytes, mediaType, req.Filename, ct);
        var handle = await _media.UploadFromS3Async(bucket, key, ct);
        return (handle, null, key);
    }

    private static WhatsAppTemplateParameter HeaderParam(string mediaType, string? id, string? link)
    {
        var media = new WhatsAppTemplateMedia { Id = id, Link = link };
        return mediaType == "video"
            ? new WhatsAppTemplateParameter { Type = "video", Video = media }
            : new WhatsAppTemplateParameter { Type = "image", Image = media };
    }

    private static WhatsAppMessage BuildMediaMessage(string mediaType, string to, string id, WhatsAppOutboundMedia body) =>
        mediaType == "video"
            ? new WhatsAppVideoMessage { To = to, BizOpaqueCallbackData = id, Video = body }
            : new WhatsAppImageMessage { To = to, BizOpaqueCallbackData = id, Image = body };

    private async Task PersistOutboundAsync(
        string id, string waId, string kind, string preview, string? awsId, string sentBy,
        string? mediaS3Key, string? mediaUrl, string? templateName, CancellationToken ct)
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
            TemplateName = templateName,
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
