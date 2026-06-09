namespace WhatsAppClient.Core.Exceptions;

/// <summary>
/// Thrown when AWS End User Messaging Social rejects or fails to send a WhatsApp message.
/// The original AWS SDK exception is available via <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class WhatsAppMessageException : Exception
{
    public WhatsAppMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
