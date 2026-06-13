using WhatsAppClient.Core.Models.Inbound;

namespace WhatsAppClient.Core.Services;

/// <summary>
/// Parses the JSON events that AWS End User Messaging Social publishes to the event-destination
/// SNS topic into a flattened <see cref="WhatsAppInboundEvent"/>.
/// </summary>
public interface IWhatsAppEventParser
{
    /// <summary>
    /// Parses a single AWS event. <paramref name="eventJson"/> is the AWS envelope JSON — i.e. the
    /// <c>Message</c> string of the SNS notification (after SNS/SQS unwrapping).
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">The payload is not valid AWS event JSON.</exception>
    WhatsAppInboundEvent Parse(string eventJson);
}
