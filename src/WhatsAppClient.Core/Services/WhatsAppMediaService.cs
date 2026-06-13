using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Exceptions;

namespace WhatsAppClient.Core.Services;

/// <inheritdoc />
public sealed class WhatsAppMediaService : IWhatsAppMediaService
{
    private readonly IAmazonSocialMessaging _client;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppMediaService> _logger;

    public WhatsAppMediaService(
        IAmazonSocialMessaging client,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppMediaService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WhatsAppMediaDownloadResult> DownloadToS3Async(
        string mediaId,
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var request = new GetWhatsAppMessageMediaRequest
        {
            MediaId = mediaId,
            OriginationPhoneNumberId = _options.OriginationPhoneNumberId,
            DestinationS3File = new S3File { BucketName = bucketName, Key = key },
        };

        _logger.LogInformation("Downloading WhatsApp media {MediaId} to s3://{Bucket}/{Key}", mediaId, bucketName, key);

        try
        {
            var response = await _client.GetWhatsAppMessageMediaAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return new WhatsAppMediaDownloadResult(response.MimeType, response.FileSize ?? 0);
        }
        catch (AmazonSocialMessagingException ex)
        {
            _logger.LogError(ex, "Failed to download WhatsApp media {MediaId}", mediaId);
            throw new WhatsAppMessageException($"Failed to download WhatsApp media: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> UploadFromS3Async(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var request = new PostWhatsAppMessageMediaRequest
        {
            OriginationPhoneNumberId = _options.OriginationPhoneNumberId,
            SourceS3File = new S3File { BucketName = bucketName, Key = key },
        };

        _logger.LogInformation("Uploading WhatsApp media from s3://{Bucket}/{Key}", bucketName, key);

        try
        {
            var response = await _client.PostWhatsAppMessageMediaAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return response.MediaId;
        }
        catch (AmazonSocialMessagingException ex)
        {
            _logger.LogError(ex, "Failed to upload WhatsApp media from s3://{Bucket}/{Key}", bucketName, key);
            throw new WhatsAppMessageException($"Failed to upload WhatsApp media: {ex.Message}", ex);
        }
    }
}
