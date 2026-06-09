namespace WhatsAppClient.Lambda.Models;

/// <summary>
/// The result returned by the WhatsApp send-message Lambda function.
/// </summary>
public sealed class SendMessageOutput
{
    public required bool Success { get; init; }

    /// <summary>
    /// Set when <see cref="Success"/> is true: the message identifier returned by
    /// AWS End User Messaging Social.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Set when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
