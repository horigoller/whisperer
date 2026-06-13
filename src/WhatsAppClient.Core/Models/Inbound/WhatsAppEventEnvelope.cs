using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models.Inbound;

/// <summary>
/// The AWS End User Messaging Social event published to the event-destination SNS topic when
/// a customer sends a message or a message status changes. The Meta webhook payload itself is
/// carried, as a JSON <em>string</em>, in <see cref="WhatsAppWebhookEntry"/>.
/// See https://docs.aws.amazon.com/social-messaging/latest/userguide/managing-event-destination-dlrs.html.
/// </summary>
public sealed class WhatsAppEventEnvelope
{
    [JsonPropertyName("context")]
    public WhatsAppEventContext? Context { get; init; }

    /// <summary>
    /// The Meta webhook <c>entry</c> object, received as a JSON-encoded string. Decode it into a
    /// <see cref="WhatsAppWebhookEntry"/> to read the messages and statuses it carries.
    /// </summary>
    [JsonPropertyName("whatsAppWebhookEntry")]
    public string? WhatsAppWebhookEntry { get; init; }

    [JsonPropertyName("aws_account_id")]
    public string? AwsAccountId { get; init; }

    [JsonPropertyName("message_timestamp")]
    public string? MessageTimestamp { get; init; }

    /// <summary>The AWS-assigned identifier for this event.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}

public sealed class WhatsAppEventContext
{
    [JsonPropertyName("MetaWabaIds")]
    public IReadOnlyList<WhatsAppWabaId>? MetaWabaIds { get; init; }

    [JsonPropertyName("MetaPhoneNumberIds")]
    public IReadOnlyList<WhatsAppPhoneNumberId>? MetaPhoneNumberIds { get; init; }
}

public sealed class WhatsAppWabaId
{
    [JsonPropertyName("wabaId")]
    public string? WabaId { get; init; }

    [JsonPropertyName("arn")]
    public string? Arn { get; init; }
}

public sealed class WhatsAppPhoneNumberId
{
    [JsonPropertyName("metaPhoneNumberId")]
    public string? MetaPhoneNumberId { get; init; }

    [JsonPropertyName("arn")]
    public string? Arn { get; init; }
}
