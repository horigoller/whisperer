using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// A free-form text message. Free-form messages can only be sent within the 24-hour
/// customer service window opened by an inbound message from the recipient.
/// </summary>
public sealed class WhatsAppTextMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public required WhatsAppTextBody Text { get; init; }
}

public sealed class WhatsAppTextBody
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    /// <summary>
    /// When true, WhatsApp will render the first URL found in <see cref="Body"/> as a link preview.
    /// </summary>
    [JsonPropertyName("preview_url")]
    public bool PreviewUrl { get; init; }
}
