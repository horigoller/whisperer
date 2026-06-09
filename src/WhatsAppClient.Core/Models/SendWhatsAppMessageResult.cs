namespace WhatsAppClient.Core.Models;

/// <summary>
/// The result of successfully submitting a WhatsApp message via AWS End User Messaging Social.
/// </summary>
/// <param name="MessageId">
/// The identifier AWS assigns to the submitted message, returned by the
/// SendWhatsAppMessage API operation.
/// </param>
public sealed record SendWhatsAppMessageResult(string MessageId);
