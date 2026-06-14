using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// Base class for the JSON payloads sent to the WhatsApp Business Platform "messages" endpoint.
/// AWS End User Messaging Social passes this JSON through verbatim to Meta, see
/// https://developers.facebook.com/docs/whatsapp/cloud-api/reference/messages.
/// </summary>
public abstract class WhatsAppMessage
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; init; } = "whatsapp";

    [JsonPropertyName("recipient_type")]
    public string RecipientType { get; init; } = "individual";

    /// <summary>
    /// The recipient's WhatsApp phone number in E.164 format (e.g. "+15551234567").
    /// </summary>
    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>
    /// Optional opaque correlation string (max 512 chars) echoed back verbatim in the message's
    /// status webhooks (<c>statuses[].biz_opaque_callback_data</c>). Use it to match a delivery
    /// status to the outbound record you stored, since the send API's returned id differs from
    /// the Meta <c>wamid</c> carried by status updates.
    /// </summary>
    [JsonPropertyName("biz_opaque_callback_data")]
    public string? BizOpaqueCallbackData { get; init; }
}
