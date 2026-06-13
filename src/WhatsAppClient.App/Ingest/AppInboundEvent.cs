using System.Text.Json.Serialization;
using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.App.Ingest;

/// <summary>
/// The EventBridge event published by the receive pipeline (EventBridgeInboundEventPublisher).
/// Wrapper keys are PascalCase; the nested message/status reuse Core's Meta-shaped models.
/// </summary>
public sealed class AppInboundEvent
{
    [JsonPropertyName("detail-type")]
    public string? DetailType { get; init; }

    [JsonPropertyName("detail")]
    public AppInboundDetail? Detail { get; init; }
}

public sealed class AppInboundDetail
{
    [JsonPropertyName("EventId")]
    public string? EventId { get; init; }

    [JsonPropertyName("ContactName")]
    public string? ContactName { get; init; }

    [JsonPropertyName("MediaS3Key")]
    public string? MediaS3Key { get; init; }

    [JsonPropertyName("Message")]
    public WhatsAppInboundMessage? Message { get; init; }

    [JsonPropertyName("Status")]
    public WhatsAppStatus? Status { get; init; }
}
