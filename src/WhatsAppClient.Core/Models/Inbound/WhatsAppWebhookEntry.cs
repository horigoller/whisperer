using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models.Inbound;

/// <summary>
/// The decoded Meta webhook <c>entry</c> object carried in
/// <see cref="WhatsAppEventEnvelope.WhatsAppWebhookEntry"/>. Mirrors the
/// WhatsApp Business Platform Cloud API webhook notification payload, see
/// https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks/components.
/// </summary>
public sealed class WhatsAppWebhookEntry
{
    /// <summary>The WhatsApp Business Account (WABA) id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("changes")]
    public IReadOnlyList<WhatsAppChange>? Changes { get; init; }
}

public sealed class WhatsAppChange
{
    [JsonPropertyName("value")]
    public WhatsAppChangeValue? Value { get; init; }

    /// <summary>The notification type, e.g. "messages".</summary>
    [JsonPropertyName("field")]
    public string? Field { get; init; }
}

public sealed class WhatsAppChangeValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; init; }

    [JsonPropertyName("metadata")]
    public WhatsAppValueMetadata? Metadata { get; init; }

    [JsonPropertyName("contacts")]
    public IReadOnlyList<WhatsAppContact>? Contacts { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<WhatsAppInboundMessage>? Messages { get; init; }

    [JsonPropertyName("statuses")]
    public IReadOnlyList<WhatsAppStatus>? Statuses { get; init; }
}

public sealed class WhatsAppValueMetadata
{
    /// <summary>The WABA phone number in display format, e.g. "14255550123".</summary>
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>The Meta phone number id that received the message.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

public sealed class WhatsAppContact
{
    [JsonPropertyName("profile")]
    public WhatsAppContactProfile? Profile { get; init; }

    /// <summary>The customer's WhatsApp id (their phone number without a leading "+").</summary>
    [JsonPropertyName("wa_id")]
    public string? WaId { get; init; }
}

public sealed class WhatsAppContactProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
