using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>An outbound image message. Like all free-form messages, deliverable only within the 24h window.</summary>
public sealed class WhatsAppImageMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "image";

    [JsonPropertyName("image")]
    public required WhatsAppOutboundMedia Image { get; init; }
}

/// <summary>An outbound document message.</summary>
public sealed class WhatsAppDocumentMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "document";

    [JsonPropertyName("document")]
    public required WhatsAppOutboundMedia Document { get; init; }
}

/// <summary>An outbound video message.</summary>
public sealed class WhatsAppVideoMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "video";

    [JsonPropertyName("video")]
    public required WhatsAppOutboundMedia Video { get; init; }
}

/// <summary>An outbound audio message.</summary>
public sealed class WhatsAppAudioMessage : WhatsAppMessage
{
    [JsonPropertyName("type")]
    public override string Type => "audio";

    [JsonPropertyName("audio")]
    public required WhatsAppOutboundMedia Audio { get; init; }
}
