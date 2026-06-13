using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.ReceiveLambda.Events;

/// <summary>Re-publishes parsed inbound events so other services can subscribe (fan-out).</summary>
public interface IInboundEventPublisher
{
    Task PublishMessageAsync(
        WhatsAppInboundEvent context,
        WhatsAppInboundMessage message,
        string? mediaS3Key,
        CancellationToken cancellationToken = default);

    Task PublishStatusAsync(
        WhatsAppInboundEvent context,
        WhatsAppStatus status,
        CancellationToken cancellationToken = default);
}
