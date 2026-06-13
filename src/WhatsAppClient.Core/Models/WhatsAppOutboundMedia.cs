using System.Text.Json.Serialization;

namespace WhatsAppClient.Core.Models;

/// <summary>
/// The media body of an outbound media message (image, document, video, audio). Set exactly one
/// of <see cref="Id"/> (a handle returned by PostWhatsAppMessageMedia) or <see cref="Link"/>
/// (a publicly reachable HTTPS URL).
/// </summary>
public sealed class WhatsAppOutboundMedia
{
    /// <summary>The media handle returned by AWS End User Messaging Social PostWhatsAppMessageMedia.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>A publicly reachable HTTPS URL to the media, as an alternative to <see cref="Id"/>.</summary>
    [JsonPropertyName("link")]
    public string? Link { get; init; }

    /// <summary>Optional caption (images, videos, documents).</summary>
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    /// <summary>Optional filename, used for documents.</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }
}
