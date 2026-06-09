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
}
