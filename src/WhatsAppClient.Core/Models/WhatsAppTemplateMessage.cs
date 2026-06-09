using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// A pre-approved template message. Template messages can be sent at any time, including
/// outside the 24-hour customer service window, and are required for the first message
/// to a recipient.
/// </summary>
public sealed class WhatsAppTemplateMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "template";

    [JsonPropertyName("template")]
    public required WhatsAppTemplate Template { get; init; }
}

public sealed class WhatsAppTemplate
{
    /// <summary>
    /// The name of the template, as configured in the WhatsApp Business Manager.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("language")]
    public required WhatsAppTemplateLanguage Language { get; init; }

    /// <summary>
    /// Optional placeholder values for the template's header, body, and button components.
    /// </summary>
    [JsonPropertyName("components")]
    public IReadOnlyList<WhatsAppTemplateComponent>? Components { get; init; }
}

public sealed class WhatsAppTemplateLanguage
{
    /// <summary>
    /// The template's language and locale code, e.g. "en_US".
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}
