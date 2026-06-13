using System.ComponentModel.DataAnnotations;

namespace WhatsAppClient.ReceiveLambda.Configuration;

/// <summary>
/// Configuration for the inbound (receive) pipeline, bound from the "Receive" configuration
/// section (environment variables prefixed <c>Receive__</c> on Lambda).
/// </summary>
public sealed class ReceiveOptions
{
    public const string SectionName = "Receive";

    /// <summary>The DynamoDB table that stores inbound messages and statuses.</summary>
    [Required]
    public required string MessagesTableName { get; set; }

    /// <summary>The S3 bucket inbound media is downloaded into. Required when <see cref="DownloadMedia"/> is true.</summary>
    public string? MediaBucketName { get; set; }

    /// <summary>The EventBridge bus normalized events are published to. Required when <see cref="PublishEvents"/> is true.</summary>
    public string? EventBusName { get; set; }

    /// <summary>Whether to mark inbound messages as read (blue ticks). Defaults to true.</summary>
    public bool MarkAsRead { get; set; } = true;

    /// <summary>Whether to download inbound media to S3. Defaults to true.</summary>
    public bool DownloadMedia { get; set; } = true;

    /// <summary>Whether to publish normalized events to EventBridge. Defaults to true.</summary>
    public bool PublishEvents { get; set; } = true;
}
