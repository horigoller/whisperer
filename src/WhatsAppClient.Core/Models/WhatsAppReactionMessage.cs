using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// A reaction (emoji) applied to a message the recipient previously sent you. Reactions can be
/// sent within the 24-hour customer service window. Send an empty <see cref="WhatsAppReaction.Emoji"/>
/// to remove a previously sent reaction.
/// </summary>
public sealed class WhatsAppReactionMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "reaction";

    [JsonPropertyName("reaction")]
    public required WhatsAppReaction Reaction { get; init; }
}

public sealed class WhatsAppReaction
{
    /// <summary>The id of the message being reacted to ("wamid...").</summary>
    [JsonPropertyName("message_id")]
    public required string MessageId { get; init; }

    /// <summary>The reaction emoji, e.g. "👍". Use an empty string to remove a reaction.</summary>
    [JsonPropertyName("emoji")]
    public required string Emoji { get; init; }
}
