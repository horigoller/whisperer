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

    /// <summary>Only messages newer than <paramref name="afterCreatedAt"/> — for cheap incremental polling.</summary>
    public Task<IReadOnlyList<ChatMessage>> GetNewMessagesAsync(
        string waId, string afterCreatedAt, CancellationToken ct = default) =>
        _repo.ListMessagesAfterAsync(waId, afterCreatedAt, ct);

    /// <summary>Free-form reply; only allowed while the 24h window is open.</summary>
    public async Task<ChatMessage> ReplyAsync(string waId, string text, string sentBy, CancellationToken ct = default)
    {
        var contact = await _repo.GetContactAsync(waId, ct) ?? throw new ContactNotFoundException(waId);
        var conversation = await _repo.GetConversationAsync(waId, ct);
        if (!WindowOpen(conversation)) throw new WindowClosedException();

        var id = Guid.NewGuid().ToString();
        // Stamp our id as biz_opaque_callback_data so status webhooks can be correlated back to it.
        var message = new WhatsAppTextMessage
        {
            To = contact.PhoneE164,
            Text = new WhatsAppTextBody { Body = text },
            BizOpaqueCallbackData = id,
        };
        var awsId = (await _whatsapp.SendMessageAsync(message, ct)).MessageId;
        return await PersistOutboundAsync(id, waId, "text", text, awsId, sentBy, templateName: null, ct);
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

        var id = Guid.NewGuid().ToString();
        var message = new WhatsAppTemplateMessage
        {
            To = contact.PhoneE164,
            Template = new WhatsAppTemplate
            {
                Name = templateName,
                Language = new WhatsAppTemplateLanguage { Code = languageCode },
                Components = components,
            },
            BizOpaqueCallbackData = id,
        };
        var awsId = (await _whatsapp.SendMessageAsync(message, ct)).MessageId;
        return await PersistOutboundAsync(id, waId, "template", $"[template: {templateName}]", awsId, sentBy, templateName, ct);
    }

    private static bool WindowOpen(Conversation? conv) =>
        conv?.WindowExpiresAt is { } exp &&
        DateTimeOffset.TryParse(exp, out var when) && when > DateTimeOffset.UtcNow;

    private async Task<ChatMessage> PersistOutboundAsync(
        string id, string waId, string type, string text, string? awsId, string sentBy, string? templateName, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("o");
        var msg = new ChatMessage
        {
            WaId = waId,
            Id = id,
            Direction = "out",
            Type = type,
            Text = text,
            Status = "sent",
            WaMessageId = awsId,
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
