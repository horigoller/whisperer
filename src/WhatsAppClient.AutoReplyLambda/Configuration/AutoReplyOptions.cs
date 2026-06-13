namespace WhatsAppClient.AutoReplyLambda.Configuration;

/// <summary>Configuration for the auto-reply, bound from the "AutoReply" section.</summary>
public sealed class AutoReplyOptions
{
    public const string SectionName = "AutoReply";

    /// <summary>The acknowledgement text sent back to a customer who messages in.</summary>
    public string Message { get; set; } =
        "Thanks for your message! 👋 We've received it and someone from Goller's Whisperer will get back to you shortly.";
}
