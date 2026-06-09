using System.ComponentModel.DataAnnotations;

namespace WhatsAppClient.Core.Configuration;

/// <summary>
/// Configuration for sending messages through AWS End User Messaging Social.
/// </summary>
public sealed class WhatsAppOptions
{
    /// <summary>
    /// The configuration section name used when binding from <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// The phone number identifier of the WhatsApp Business Account phone number that has
    /// been linked to AWS End User Messaging Social, formatted as
    /// "phone-number-id-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx". Use the GetLinkedWhatsAppBusinessAccount
    /// API operation to find this value.
    /// </summary>
    [Required]
    public required string OriginationPhoneNumberId { get; set; }

    /// <summary>
    /// The Meta Graph API version used to format outgoing messages, e.g. "v21.0".
    /// See https://docs.aws.amazon.com/general/latest/gr/end-user-messaging.html for the
    /// versions supported by AWS End User Messaging Social in each Region.
    /// </summary>
    [Required]
    public string MetaApiVersion { get; set; } = "v21.0";
}
