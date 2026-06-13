using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// Marks an inbound message as read, displaying the blue read-receipt ticks on the customer's
/// device. Unlike <see cref="WhatsAppMessage"/> this is not addressed to a recipient — it
/// references the message being acknowledged. Sent through the same SendWhatsAppMessage API.
/// </summary>
public sealed class WhatsAppReadReceipt
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; init; } = "whatsapp";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "read";

    /// <summary>The id of the inbound message to mark as read ("wamid...").</summary>
    [JsonPropertyName("message_id")]
    public required string MessageId { get; init; }
}
