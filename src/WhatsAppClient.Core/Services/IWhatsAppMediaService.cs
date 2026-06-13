namespace WhatsAppClient.Core.Services;

/// <summary>
/// Downloads inbound WhatsApp media to Amazon S3 and uploads outbound media for sending,
/// via the AWS End User Messaging Social GetWhatsAppMessageMedia / PostWhatsAppMessageMedia APIs.
/// </summary>
public interface IWhatsAppMediaService
{
    /// <summary>
    /// Downloads the media identified by <paramref name="mediaId"/> directly into S3. AWS writes
    /// the object to <paramref name="bucketName"/>/<paramref name="key"/>; the bytes never pass
    /// through the caller.
    /// </summary>
    /// <param name="mediaId">The media id from an inbound message (<c>WhatsAppMediaInfo.Id</c>).</param>
    /// <param name="bucketName">The destination S3 bucket.</param>
    /// <param name="key">The destination S3 object key.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<WhatsAppMediaDownloadResult> DownloadToS3Async(
        string mediaId,
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads media that already lives in S3 to WhatsApp and returns the resulting media handle,
    /// which can be used as the <c>id</c> of a media template parameter or media message.
    /// </summary>
    Task<string> UploadFromS3Async(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of downloading inbound media to S3.</summary>
/// <param name="MimeType">The MIME type AWS reported for the media.</param>
/// <param name="FileSize">The size in bytes of the downloaded object.</param>
public sealed record WhatsAppMediaDownloadResult(string? MimeType, long FileSize);
