using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.ReceiveLambda.Persistence;

/// <summary>Persists inbound WhatsApp messages and status updates.</summary>
public interface IInboundMessageStore
{
    /// <summary>Stores a single inbound message, optionally with the S3 key of its downloaded media.</summary>
    Task SaveMessageAsync(
        WhatsAppInboundEvent context,
        WhatsAppInboundMessage message,
        string? mediaS3Key,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a delivery-receipt / status update for a message you sent.</summary>
    Task SaveStatusAsync(
        WhatsAppInboundEvent context,
        WhatsAppStatus status,
        CancellationToken cancellationToken = default);
}
