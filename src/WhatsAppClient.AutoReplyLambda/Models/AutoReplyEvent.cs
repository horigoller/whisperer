using System.Text.Json.Serialization;
using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.AutoReplyLambda.Models;

/// <summary>
/// The EventBridge event delivered to this Lambda. We only care about <c>detail</c>, which is the
/// normalized payload published by the receive pipeline's <c>EventBridgeInboundEventPublisher</c>.
/// </summary>
public sealed class AutoReplyEvent
{
    [JsonPropertyName("detail")]
    public AutoReplyDetail? Detail { get; init; }
}

public sealed class AutoReplyDetail
{
    [JsonPropertyName("ContactName")]
    public string? ContactName { get; init; }

    [JsonPropertyName("Message")]
    public WhatsAppInboundMessage? Message { get; init; }
}

/// <summary>The handler result (for logging/observability; EventBridge ignores the return value).</summary>
public sealed class AutoReplyResult
{
    public required bool Replied { get; init; }
    public string? MessageId { get; init; }
    public string? ErrorMessage { get; init; }
}
