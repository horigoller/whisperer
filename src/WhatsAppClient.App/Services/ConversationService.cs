using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.App.Services;

public sealed class ConversationService
{
    private readonly IAppRepository _repo;
    private readonly IWhatsAppMessageService _whatsapp;

    public ConversationService(IAppRepository repo, IWhatsAppMessageService whatsapp)
    {
        _repo = repo;
        _whatsapp = whatsapp;
    }

    public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct = default) =>
        _repo.ListConversationsAsync(ct);

    public async Task<(IReadOnlyList<ChatMessage> Messages, Conversation? Conversation)> GetThreadAsync(
        string waId, CancellationToken ct = default)
    {
        var messagesTask = _repo.ListMessagesAsync(waId, ct);
        var conversationTask = _repo.GetConversationAsync(waId, ct);
        await Task.WhenAll(messagesTask, conversationTask);
        var conversation = conversationTask.Result;

        if (conversation is { Unread: > 0 })
        {
            // Atomic SET so a concurrent inbound's unread++ isn't clobbered by this reset.
            await _repo.ResetConversationUnreadAsync(waId, ct);
            conversation.Unread = 0;
        }
        return (messagesTask.Result, conversation);
    }

    /// <summary>Free-form reply; only allowed while the 24h window is open.</summary>
    public async Task<ChatMessage> ReplyAsync(string waId, string text, string sentBy, CancellationToken ct = default)
    {
        var contact = await _repo.GetContactAsync(waId, ct) ?? throw new ContactNotFoundException(waId);
        var conversation = await _repo.GetConversationAsync(waId, ct);
        if (!WindowOpen(conversation)) throw new WindowClosedException();

        var waMessageId = (await _whatsapp.SendTextMessageAsync(contact.PhoneE164, text, cancellationToken: ct)).MessageId;
        return await PersistOutboundAsync(waId, "text", text, waMessageId, sentBy, templateName: null, ct);
    }

    /// <summary>Template send; works outside the 24h window (agent-initiated utility exchange).</summary>
    public async Task<ChatMessage> SendTemplateAsync(
        string waId, string templateName, string languageCode, IReadOnlyList<string> bodyParams,
        string sentBy, CancellationToken ct = default)
    {
        var contact = await _repo.GetContactAsync(waId, ct) ?? throw new ContactNotFoundException(waId);

        IReadOnlyList<WhatsAppTemplateComponent>? components = bodyParams.Count > 0
            ? new[]
            {
                new WhatsAppTemplateComponent
                {
                    Type = "body",
                    Parameters = bodyParams.Select(WhatsAppTemplateParameter.FromText).ToList(),
                },
            }
            : null;

        var waMessageId = (await _whatsapp.SendTemplateMessageAsync(
            contact.PhoneE164, templateName, languageCode, components, ct)).MessageId;
        return await PersistOutboundAsync(waId, "template", $"[template: {templateName}]", waMessageId, sentBy, templateName, ct);
    }

    private static bool WindowOpen(Conversation? conv) =>
        conv?.WindowExpiresAt is { } exp &&
        DateTimeOffset.TryParse(exp, out var when) && when > DateTimeOffset.UtcNow;

    private async Task<ChatMessage> PersistOutboundAsync(
        string waId, string type, string text, string? waMessageId, string sentBy, string? templateName, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("o");
        var msg = new ChatMessage
        {
            WaId = waId,
            Id = Guid.NewGuid().ToString(),
            Direction = "out",
            Type = type,
            Text = text,
            Status = "sent",
            WaMessageId = waMessageId,
            SentBy = sentBy,
            TemplateName = templateName,
            CreatedAt = now,
        };
        await _repo.PutMessageAsync(msg, ct);

        var existingTask = _repo.GetConversationAsync(waId, ct);
        var contactTask = _repo.GetContactAsync(waId, ct);
        await Task.WhenAll(existingTask, contactTask);
        var existing = existingTask.Result;
        var contact = contactTask.Result;
        await _repo.PutConversationAsync(new Conversation
        {
            WaId = waId,
            Name = existing?.Name ?? contact?.Name,
            LastPreview = text,
            LastActivityAt = now,
            WindowExpiresAt = existing?.WindowExpiresAt,
            Unread = existing?.Unread ?? 0,
        }, ct);
        return msg;
    }
}
