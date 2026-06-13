namespace WhatsAppClient.Core.Models.Inbound;

/// <summary>
/// A flattened, consumer-friendly view of a single AWS event: the messages and statuses it
/// carried, plus the metadata needed to act on them. Produced by
/// <see cref="Services.IWhatsAppEventParser"/>.
/// </summary>
public sealed class WhatsAppInboundEvent
{
    /// <summary>The AWS-assigned id for the event, from the SNS envelope.</summary>
    public string? EventId { get; init; }

    /// <summary>The WABA id (Meta business account id).</summary>
    public string? WabaId { get; init; }

    /// <summary>The Meta phone number id that received the messages.</summary>
    public string? PhoneNumberId { get; init; }

    /// <summary>The WABA phone number in display format.</summary>
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>Inbound messages sent by customers.</summary>
    public IReadOnlyList<WhatsAppInboundMessage> Messages { get; init; } = [];

    /// <summary>Delivery-receipt / status updates for messages you sent.</summary>
    public IReadOnlyList<WhatsAppStatus> Statuses { get; init; } = [];

    /// <summary>Customer profiles keyed by their WhatsApp id (<c>wa_id</c>).</summary>
    public IReadOnlyDictionary<string, WhatsAppContact> Contacts { get; init; } =
        new Dictionary<string, WhatsAppContact>();

    /// <summary>Resolves the display name for a sender's WhatsApp id, if WhatsApp provided one.</summary>
    public string? ResolveContactName(string? waId) =>
        waId is not null && Contacts.TryGetValue(waId, out var contact) ? contact.Profile?.Name : null;
}
