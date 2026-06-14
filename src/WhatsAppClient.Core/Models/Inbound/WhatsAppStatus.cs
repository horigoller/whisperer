using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models.Inbound;

/// <summary>
/// A delivery-receipt / status update for a message you sent: "sent", "delivered", "read",
/// or "failed". See https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks/components.
/// </summary>
public sealed class WhatsAppStatus
{
    /// <summary>The id of the message this status refers to.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>One of "sent", "delivered", "read", "failed".</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Unix epoch seconds, as a string.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>The recipient's WhatsApp id.</summary>
    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; init; }

    /// <summary>The opaque correlation string set on the outbound message, echoed back here.</summary>
    [JsonPropertyName("biz_opaque_callback_data")]
    public string? BizOpaqueCallbackData { get; init; }

    [JsonPropertyName("conversation")]
    public WhatsAppStatusConversation? Conversation { get; init; }

    [JsonPropertyName("pricing")]
    public WhatsAppStatusPricing? Pricing { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<WhatsAppStatusError>? Errors { get; init; }
}

public sealed class WhatsAppStatusConversation
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("expiration_timestamp")]
    public string? ExpirationTimestamp { get; init; }

    [JsonPropertyName("origin")]
    public WhatsAppStatusConversationOrigin? Origin { get; init; }
}

public sealed class WhatsAppStatusConversationOrigin
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class WhatsAppStatusPricing
{
    [JsonPropertyName("billable")]
    public bool? Billable { get; init; }

    [JsonPropertyName("pricing_model")]
    public string? PricingModel { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }
}

public sealed class WhatsAppStatusError
{
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
