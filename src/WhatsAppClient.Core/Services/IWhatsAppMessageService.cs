using WhatsAppClient.Core.Models;

namespace WhatsAppClient.Core.Services;

/// <summary>
/// Sends WhatsApp Business Platform messages through AWS End User Messaging Social.
/// </summary>
public interface IWhatsAppMessageService
{
    /// <summary>
    /// Sends a free-form text message. This can only be delivered while the recipient's
    /// 24-hour customer service window is open.
    /// </summary>
    /// <param name="to">The recipient's phone number in E.164 format, e.g. "+15551234567".</param>
    /// <param name="body">The message text. Must be 1-4096 characters.</param>
    /// <param name="previewUrl">Whether to render a link preview for the first URL in <paramref name="body"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<SendWhatsAppMessageResult> SendTextMessageAsync(
        string to,
        string body,
        bool previewUrl = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a pre-approved template message. Templates can be sent at any time, including
    /// to open a new 24-hour customer service window.
    /// </summary>
    /// <param name="to">The recipient's phone number in E.164 format, e.g. "+15551234567".</param>
    /// <param name="templateName">The name of the template, as configured in WhatsApp Business Manager.</param>
    /// <param name="languageCode">The template's language and locale code, e.g. "en_US".</param>
    /// <param name="components">Optional placeholder values for the template's components.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<SendWhatsAppMessageResult> SendTemplateMessageAsync(
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<WhatsAppTemplateComponent>? components = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an arbitrary <see cref="WhatsAppMessage"/> payload.
    /// </summary>
    Task<SendWhatsAppMessageResult> SendMessageAsync(
        WhatsAppMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a media message (image, document, video, or audio). Provide exactly one of
    /// <paramref name="mediaId"/> (a handle from PostWhatsAppMessageMedia) or <paramref name="link"/>
    /// (a public HTTPS URL). Like text, deliverable only within the 24h customer service window.
    /// </summary>
    /// <param name="to">The recipient's phone number in E.164 format.</param>
    /// <param name="mediaType">One of "image", "document", "video", "audio".</param>
    Task<SendWhatsAppMessageResult> SendMediaMessageAsync(
        string to,
        string mediaType,
        string? mediaId = null,
        string? link = null,
        string? caption = null,
        string? filename = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an inbound message as read, showing the blue read-receipt ticks on the customer's
    /// device. Pass the <c>id</c> of a received message.
    /// </summary>
    Task<SendWhatsAppMessageResult> MarkMessageReadAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacts to a message the recipient previously sent you with an emoji. Pass an empty
    /// <paramref name="emoji"/> to remove a previously sent reaction.
    /// </summary>
    /// <param name="to">The recipient's phone number in E.164 format, e.g. "+15551234567".</param>
    /// <param name="messageId">The id of the message being reacted to ("wamid...").</param>
    /// <param name="emoji">The reaction emoji, e.g. "👍".</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<SendWhatsAppMessageResult> SendReactionAsync(
        string to,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default);
}
