using Microsoft.Extensions.Logging;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Realtime;
using WhatsAppClient.App.Services;
using WhatsAppClient.App.Util;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.App.Ingest;

/// <summary>
/// Applies inbound EventBridge events to the app table: self-registers contacts, appends inbound
/// messages, refreshes the conversation + 24h window, and patches outbound message statuses.
/// Decoupled from the Lambda runtime for testing.
/// </summary>
public sealed class AppIngestProcessor
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly IAppRepository _repo;
    private readonly IRealtimePublisher _realtime;
    private readonly IWhatsAppMessageService _whatsapp;
    private readonly IHaCallbackClient _ha;
    private readonly ILogger<AppIngestProcessor> _logger;

    public AppIngestProcessor(
        IAppRepository repo,
        IRealtimePublisher realtime,
        IWhatsAppMessageService whatsapp,
        IHaCallbackClient ha,
        ILogger<AppIngestProcessor> logger)
    {
        _repo = repo;
        _realtime = realtime;
        _whatsapp = whatsapp;
        _ha = ha;
        _logger = logger;
    }

    public async Task ProcessAsync(AppInboundEvent evt, CancellationToken ct = default)
    {
        switch (evt.DetailType)
        {
            case "MessageReceived" when evt.Detail?.Message is { } message:
                await IngestMessageAsync(message, evt.Detail.ContactName, evt.Detail.MediaS3Key, ct);
                if (message.Type == "button" && !string.IsNullOrEmpty(message.Button?.Payload))
                    await HandleButtonTapAsync(message, ct);
                break;

            case "StatusUpdated" when evt.Detail?.Status is { Status: { } status } s:
                // Correlate by the opaque id we stamped on send; the status also carries the Meta wamid.
                if (string.IsNullOrEmpty(s.BizOpaqueCallbackData))
                {
                    _logger.LogInformation("Status {Status} for {Id} has no biz_opaque_callback_data; cannot correlate", status, s.Id);
                    break;
                }
                var error = s.Errors?.FirstOrDefault();
                var errorDetail = error?.ErrorData?.Details ?? error?.Message ?? error?.Title;
                var matched = await _repo.PatchMessageStatusByRefAsync(
                    s.BizOpaqueCallbackData, status, s.Id, error?.Code, errorDetail, ct);
                if (matched)
                {
                    var waId = PhoneNumbers.ToWaId(s.RecipientId ?? string.Empty);
                    if (waId.Length > 0)
                    {
                        await _realtime.PublishAsync(new RealtimeEvent(
                            "status", waId, MessageId: s.BizOpaqueCallbackData, Status: status,
                            ErrorCode: error?.Code, ErrorDetail: errorDetail), ct);
                    }
                }
                else if (status == "failed")
                {
                    // Not a stored conversation message — may be a login code (opaque == challengeId).
                    await _repo.PatchAuthDeliveryErrorAsync(s.BizOpaqueCallbackData, error?.Code, errorDetail, ct);
                }
                else
                {
                    _logger.LogInformation("No stored message for opaque ref {Ref}", s.BizOpaqueCallbackData);
                }
                break;

            default:
                _logger.LogInformation("Ignoring event detail-type={DetailType}", evt.DetailType);
                break;
        }
    }

    // Emoji reactions acknowledging a button tap on the recipient's phone.
    private const string ReactHandled = "✅";
    private const string ReactIgnored = "🚫";
    private const string ReactExpired = "⌛";
    private const string ReactFailed = "⚠️";

    /// <summary>
    /// Resolves a tapped quick-reply button to its binding and acts: "Handle It" POSTs to the HA
    /// webhook if the binding is still active and unexpired; "Ignore" disables it. The recipient
    /// sees an emoji reaction on the original alert for feedback. Best-effort — never throws.
    /// </summary>
    private async Task HandleButtonTapAsync(WhatsAppInboundMessage message, CancellationToken ct)
    {
        var payload = message.Button!.Payload!;
        var sep = payload.IndexOf(':');
        if (sep <= 0)
        {
            _logger.LogInformation("Button payload {Payload} is not an action binding; ignoring", payload);
            return;
        }
        var kind = payload[..sep];      // "H" (handle) or "I" (ignore)
        var token = payload[(sep + 1)..];

        var binding = await _repo.GetButtonBindingAsync(token, ct);
        if (binding is null)
        {
            _logger.LogInformation("No active binding for button token {Token}", token);
            return;
        }

        var waId = PhoneNumbers.ToWaId(message.From ?? string.Empty);
        var to = waId.Length > 0 ? $"+{waId}" : null;
        var alertId = message.Context?.Id;   // the wamid of the alert carrying the button, for reactions
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (kind == "I")
        {
            // Ignore: disable the callback so a later "Handle It" cannot fire.
            if (await _repo.TryConsumeButtonBindingAsync(token, "ignored", now, ct))
                await ReactAsync(to, alertId, ReactIgnored, ct);
            return;
        }

        if (kind != "H") return;

        // Handle It: fire exactly once, only while active and within the 5-minute window.
        if (binding.Status != "active" || now > binding.ExpiresAt
            || !await _repo.TryConsumeButtonBindingAsync(token, "consumed", now, ct))
        {
            await ReactAsync(to, alertId, ReactExpired, ct);
            return;
        }

        var posted = await _ha.PostAsync(binding.CallbackUrl, new
        {
            action = "handle",
            data = binding.Data,
            home = binding.Home,
            waId = binding.WaId,
            token,
            tappedAt = DateTimeOffset.UtcNow.ToString("o"),
        }, ct);
        await ReactAsync(to, alertId, posted ? ReactHandled : ReactFailed, ct);
    }

    private async Task ReactAsync(string? to, string? alertMessageId, string emoji, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(alertMessageId)) return;
        try
        {
            await _whatsapp.SendReactionAsync(to, alertMessageId, emoji, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to react {Emoji} to alert {AlertId}", emoji, alertMessageId);
        }
    }

    private async Task IngestMessageAsync(
        WhatsAppInboundMessage message, string? contactName, string? mediaS3Key, CancellationToken ct)
    {
        var waId = PhoneNumbers.ToWaId(message.From ?? string.Empty);
        if (string.IsNullOrEmpty(waId))
        {
            _logger.LogWarning("Inbound message had no sender; skipping.");
            return;
        }

        var messageTime = long.TryParse(message.Timestamp, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : DateTimeOffset.UtcNow;
        var createdAt = messageTime.UtcDateTime.ToString("o");

        var existingContact = await _repo.GetContactAsync(waId, ct);
        if (existingContact is null)
        {
            await _repo.PutContactAsync(new Contact
            {
                WaId = waId,
                PhoneE164 = $"+{waId}",
                Name = contactName,
                Source = "self",
                CreatedAt = createdAt,
            }, ct);
        }
        else if (!string.IsNullOrEmpty(contactName) && string.IsNullOrEmpty(existingContact.Name))
        {
            existingContact.Name = contactName;
            await _repo.PutContactAsync(existingContact, ct);
        }

        var text = message.Text?.Body;
        var stored = new ChatMessage
        {
            WaId = waId,
            Id = message.Id ?? Guid.NewGuid().ToString(),
            Direction = "in",
            Type = message.Type ?? "text",
            Text = text,
            MediaId = message.Media?.Id,
            MediaS3Key = mediaS3Key,
            Status = "received",
            WaMessageId = message.Id,
            CreatedAt = createdAt,
        };
        await _repo.PutMessageAsync(stored, ct);

        var existingConv = await _repo.GetConversationAsync(waId, ct);
        await _repo.PutConversationAsync(new Conversation
        {
            WaId = waId,
            Name = contactName ?? existingConv?.Name ?? existingContact?.Name,
            LastPreview = text ?? $"[{message.Type}]",
            LastActivityAt = createdAt,
            // Window runs 24h from when the customer sent the message, not from when we processed it.
            WindowExpiresAt = messageTime.Add(Window).UtcDateTime.ToString("o"),
            Unread = (existingConv?.Unread ?? 0) + 1,
        }, ct);

        // Push the inbound message so open consoles update instantly.
        await _realtime.PublishAsync(new RealtimeEvent("message", waId, Message: stored), ct);
    }
}
