namespace WhatsAppClient.Lambda.Models;

/// <summary>
/// The payload accepted by the WhatsApp send-message Lambda function.
/// </summary>
public sealed class SendMessageInput
{
    /// <summary>
    /// The recipient's WhatsApp phone number in E.164 format, e.g. "+15551234567".
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Either "text" or "template".
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Required when <see cref="MessageType"/> is "text". The message body.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Whether to render a link preview for "text" messages. Defaults to false.
    /// </summary>
    public bool PreviewUrl { get; init; }

    /// <summary>
    /// Required when <see cref="MessageType"/> is "template". The template's name.
    /// </summary>
    public string? TemplateName { get; init; }

    /// <summary>
    /// Required when <see cref="MessageType"/> is "template". The template's language/locale code, e.g. "en_US".
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Optional placeholder values for the template's components.
    /// </summary>
    public List<TemplateComponentInput>? Components { get; init; }
}

/// <summary>
/// A component (header, body, or button) of a template message.
/// </summary>
public sealed class TemplateComponentInput
{
    /// <summary>
    /// One of "header", "body", or "button".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is "button". One of "quick_reply" or "url".
    /// </summary>
    public string? SubType { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is "button". The zero-based index of the button being customized.
    /// </summary>
    public string? Index { get; init; }

    public required List<TemplateParameterInput> Parameters { get; init; }
}

/// <summary>
/// A "text" placeholder value for a template component.
/// </summary>
public sealed class TemplateParameterInput
{
    /// <summary>
    /// Currently only "text" is supported by this Lambda function. For other parameter
    /// types (currency, date_time, media), call <c>IWhatsAppMessageService</c> directly.
    /// </summary>
    public required string Type { get; init; }

    public string? Text { get; init; }
}
