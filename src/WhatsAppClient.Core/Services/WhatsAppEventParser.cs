using System.Text.Json;
using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.Core.Services;

/// <inheritdoc />
public sealed class WhatsAppEventParser : IWhatsAppEventParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public WhatsAppInboundEvent Parse(string eventJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventJson);

        var envelope = JsonSerializer.Deserialize<WhatsAppEventEnvelope>(eventJson, SerializerOptions)
            ?? throw new JsonException("Event payload deserialized to null.");

        // The Meta webhook entry is itself a JSON-encoded string inside the AWS envelope.
        var entry = string.IsNullOrWhiteSpace(envelope.WhatsAppWebhookEntry)
            ? null
            : JsonSerializer.Deserialize<WhatsAppWebhookEntry>(envelope.WhatsAppWebhookEntry, SerializerOptions);

        var messages = new List<WhatsAppInboundMessage>();
        var statuses = new List<WhatsAppStatus>();
        var contacts = new Dictionary<string, WhatsAppContact>();
        string? phoneNumberId = null;
        string? displayPhoneNumber = null;

        foreach (var change in entry?.Changes ?? [])
        {
            var value = change.Value;
            if (value is null)
            {
                continue;
            }

            phoneNumberId ??= value.Metadata?.PhoneNumberId;
            displayPhoneNumber ??= value.Metadata?.DisplayPhoneNumber;

            foreach (var contact in value.Contacts ?? [])
            {
                if (contact.WaId is not null)
                {
                    contacts[contact.WaId] = contact;
                }
            }

            if (value.Messages is not null)
            {
                messages.AddRange(value.Messages);
            }

            if (value.Statuses is not null)
            {
                statuses.AddRange(value.Statuses);
            }
        }

        return new WhatsAppInboundEvent
        {
            EventId = envelope.MessageId,
            WabaId = entry?.Id ?? envelope.Context?.MetaWabaIds?.FirstOrDefault()?.WabaId,
            PhoneNumberId = phoneNumberId ?? envelope.Context?.MetaPhoneNumberIds?.FirstOrDefault()?.MetaPhoneNumberId,
            DisplayPhoneNumber = displayPhoneNumber,
            Messages = messages,
            Statuses = statuses,
            Contacts = contacts,
        };
    }
}
