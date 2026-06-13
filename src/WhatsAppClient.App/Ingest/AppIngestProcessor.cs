using Microsoft.Extensions.Logging;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Util;
using WhatsAppClient.Core.Models.Inbound;

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
    private readonly ILogger<AppIngestProcessor> _logger;

    public AppIngestProcessor(IAppRepository repo, ILogger<AppIngestProcessor> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task ProcessAsync(AppInboundEvent evt, CancellationToken ct = default)
    {
        switch (evt.DetailType)
        {
            case "MessageReceived" when evt.Detail?.Message is { } message:
                await IngestMessageAsync(message, evt.Detail.ContactName, evt.Detail.MediaS3Key, ct);
                break;

            case "StatusUpdated" when evt.Detail?.Status is { Id: { } id, Status: { } status }:
                if (!await _repo.PatchMessageStatusByWaMessageIdAsync(id, status, ct))
                    _logger.LogInformation("No stored message for status {Status} of {Id}", status, id);
                break;

            default:
                _logger.LogInformation("Ignoring event detail-type={DetailType}", evt.DetailType);
                break;
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

        var createdAt = long.TryParse(message.Timestamp, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("o")
            : DateTime.UtcNow.ToString("o");

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
        await _repo.PutMessageAsync(new ChatMessage
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
        }, ct);

        var existingConv = await _repo.GetConversationAsync(waId, ct);
        await _repo.PutConversationAsync(new Conversation
        {
            WaId = waId,
            Name = contactName ?? existingConv?.Name ?? existingContact?.Name,
            LastPreview = text ?? $"[{message.Type}]",
            LastActivityAt = createdAt,
            WindowExpiresAt = DateTime.UtcNow.Add(Window).ToString("o"),
            Unread = (existingConv?.Unread ?? 0) + 1,
        }, ct);
    }
}
