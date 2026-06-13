namespace WhatsAppClient.App.Services;

/// <summary>Thrown when a free-form reply is attempted while the 24h window is closed.</summary>
public sealed class WindowClosedException() : Exception("The 24h customer service window is closed.");

/// <summary>Thrown when an operation targets a contact that does not exist.</summary>
public sealed class ContactNotFoundException(string waId) : Exception($"Contact {waId} not found.");
