using Amazon.Lambda.Core;
using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.ReceiveLambda.Processing;

/// <summary>
/// Applies the per-event side effects: download media, persist, mark read, and fan out.
/// </summary>
public interface IInboundMessageProcessor
{
    Task ProcessAsync(WhatsAppInboundEvent inboundEvent, ILambdaLogger logger, CancellationToken cancellationToken = default);
}
