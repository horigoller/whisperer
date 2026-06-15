using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;

namespace WhatsAppClient.App.Services;

/// <summary>Stages outbound media bytes in S3 so they can be uploaded to WhatsApp.</summary>
public interface IOutboundMediaStore
{
    /// <summary>Writes the bytes to S3 and returns the bucket/key to hand to PostWhatsAppMessageMedia.</summary>
    Task<(string Bucket, string Key)> StageAsync(byte[] bytes, string mediaType, string? filename, CancellationToken ct = default);
}

public sealed class S3OutboundMediaStore : IOutboundMediaStore
{
    private readonly IAmazonS3 _s3;
    private readonly AppOptions _options;

    public S3OutboundMediaStore(IAmazonS3 s3, IOptions<AppOptions> options)
    {
        _s3 = s3;
        _options = options.Value;
    }

    public async Task<(string Bucket, string Key)> StageAsync(byte[] bytes, string mediaType, string? filename, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.MediaBucketName))
            throw new InvalidOperationException("MediaBucketName is not configured.");

        var ext = Extension(mediaType, filename);
        var key = $"outgoing/notify/{Guid.NewGuid():N}{ext}";
        using var stream = new MemoryStream(bytes);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.MediaBucketName,
            Key = key,
            InputStream = stream,
            ContentType = ContentType(mediaType, ext),
        }, ct);
        return (_options.MediaBucketName, key);
    }

    private static string Extension(string mediaType, string? filename)
    {
        if (!string.IsNullOrEmpty(filename) && Path.HasExtension(filename)) return Path.GetExtension(filename);
        return mediaType.ToLowerInvariant() switch { "video" => ".mp4", _ => ".jpg" };
    }

    private static string ContentType(string mediaType, string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".3gp" => "video/3gpp",
        _ => mediaType.Equals("video", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "image/jpeg",
    };
}
