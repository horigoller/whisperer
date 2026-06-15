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

        var (ext, contentType) = Detect(bytes, filename, mediaType);
        var key = $"outgoing/notify/{Guid.NewGuid():N}{ext}";
        using var stream = new MemoryStream(bytes);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.MediaBucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
        }, ct);
        return (_options.MediaBucketName, key);
    }

    // Prefer the actual bytes (magic numbers), then the filename extension, then the media type,
    // so the stored object's content type matches its content.
    private static (string Ext, string ContentType) Detect(byte[] b, string? filename, string mediaType)
    {
        if (b.Length >= 12)
        {
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return (".png", "image/png");
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return (".jpg", "image/jpeg");
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return (".gif", "image/gif");
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return (".webp", "image/webp");
            if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70) return (".mp4", "video/mp4"); // ...ftyp
        }

        var fe = !string.IsNullOrEmpty(filename) && Path.HasExtension(filename)
            ? Path.GetExtension(filename).ToLowerInvariant() : null;
        return fe switch
        {
            ".png" => (".png", "image/png"),
            ".gif" => (".gif", "image/gif"),
            ".webp" => (".webp", "image/webp"),
            ".jpg" or ".jpeg" => (fe, "image/jpeg"),
            ".mp4" => (".mp4", "video/mp4"),
            ".3gp" => (".3gp", "video/3gpp"),
            ".mov" => (".mov", "video/quicktime"),
            _ => mediaType.Equals("video", StringComparison.OrdinalIgnoreCase)
                ? (".mp4", "video/mp4")
                : (".jpg", "image/jpeg"),
        };
    }
}
