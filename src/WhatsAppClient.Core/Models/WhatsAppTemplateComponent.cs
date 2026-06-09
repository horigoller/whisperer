using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// A component (header, body, or button) of a template message, carrying the
/// placeholder values that fill in the template's variables.
/// </summary>
public sealed class WhatsAppTemplateComponent
{
    /// <summary>
    /// One of "header", "body", or "button".
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is "button". One of "quick_reply" or "url".
    /// </summary>
    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is "button". The zero-based index of the button
    /// being customized, as a string.
    /// </summary>
    [JsonPropertyName("index")]
    public string? Index { get; init; }

    [JsonPropertyName("parameters")]
    public required IReadOnlyList<WhatsAppTemplateParameter> Parameters { get; init; }
}

/// <summary>
/// A single placeholder value for a template component. Only the property matching
/// <see cref="Type"/> should be set.
/// </summary>
public sealed class WhatsAppTemplateParameter
{
    /// <summary>
    /// One of "text", "currency", "date_time", "image", "document", or "video".
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("currency")]
    public WhatsAppTemplateCurrency? Currency { get; init; }

    [JsonPropertyName("date_time")]
    public WhatsAppTemplateDateTime? DateTime { get; init; }

    [JsonPropertyName("image")]
    public WhatsAppTemplateMedia? Image { get; init; }

    [JsonPropertyName("document")]
    public WhatsAppTemplateMedia? Document { get; init; }

    [JsonPropertyName("video")]
    public WhatsAppTemplateMedia? Video { get; init; }

    public static WhatsAppTemplateParameter FromText(string text) => new() { Type = "text", Text = text };
}

public sealed class WhatsAppTemplateCurrency
{
    [JsonPropertyName("fallback_value")]
    public required string FallbackValue { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// The amount, multiplied by 1000 (e.g. $20.99 is represented as 20990).
    /// </summary>
    [JsonPropertyName("amount_1000")]
    public required long Amount1000 { get; init; }
}

public sealed class WhatsAppTemplateDateTime
{
    [JsonPropertyName("fallback_value")]
    public required string FallbackValue { get; init; }
}

public sealed class WhatsAppTemplateMedia
{
    /// <summary>
    /// The media handle returned by the AWS End User Messaging Social PostWhatsAppMessageMedia
    /// operation, or a publicly reachable HTTPS link to the media.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("link")]
    public string? Link { get; init; }
}
