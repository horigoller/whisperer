using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models.Inbound;

/// <summary>
/// A single inbound message sent by a customer to your WABA phone number. Only the property
/// matching <see cref="Type"/> is populated (for example a "text" message sets
/// <see cref="Text"/>, an "image" message sets <see cref="Image"/>).
/// </summary>
public sealed class WhatsAppInboundMessage
{
    /// <summary>The sender's WhatsApp id (their phone number without a leading "+").</summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>The WhatsApp message id ("wamid..."). Use it to mark the message read or to react.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Unix epoch seconds, as a string, when WhatsApp received the message.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>One of "text", "image", "document", "audio", "video", "sticker", "reaction", etc.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public WhatsAppInboundText? Text { get; init; }

    [JsonPropertyName("image")]
    public WhatsAppMediaInfo? Image { get; init; }

    [JsonPropertyName("document")]
    public WhatsAppMediaInfo? Document { get; init; }

    [JsonPropertyName("audio")]
    public WhatsAppMediaInfo? Audio { get; init; }

    [JsonPropertyName("video")]
    public WhatsAppMediaInfo? Video { get; init; }

    [JsonPropertyName("sticker")]
    public WhatsAppMediaInfo? Sticker { get; init; }

    [JsonPropertyName("reaction")]
    public WhatsAppInboundReaction? Reaction { get; init; }

    /// <summary>Present when the customer replied to a previous message.</summary>
    [JsonPropertyName("context")]
    public WhatsAppMessageContext? Context { get; init; }

    /// <summary>
    /// Returns the media payload for whichever media-bearing type this message is, or null for
    /// non-media messages. Used by the receive pipeline to decide whether to download a file.
    /// </summary>
    [JsonIgnore]
    public WhatsAppMediaInfo? Media => Image ?? Document ?? Audio ?? Video ?? Sticker;
}

public sealed class WhatsAppInboundText
{
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}

/// <summary>
/// Media metadata in an inbound message. The actual bytes are not included; download them with
/// the GetWhatsAppMessageMedia API using <see cref="Id"/>.
/// </summary>
public sealed class WhatsAppMediaInfo
{
    /// <summary>The media id to pass to GetWhatsAppMessageMedia.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    /// <summary>Original filename, present for documents.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }
}

public sealed class WhatsAppInboundReaction
{
    /// <summary>The id of the message the customer reacted to.</summary>
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; init; }
}

public sealed class WhatsAppMessageContext
{
    /// <summary>The id of the message being replied to.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }
}
