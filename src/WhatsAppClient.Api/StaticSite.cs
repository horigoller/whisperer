using Amazon.S3;
using Amazon.S3.Model;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;

namespace WhatsAppClient.Api;

/// <summary>Serves the built React SPA from S3; unknown non-asset paths fall back to index.html.</summary>
public sealed class StaticSite
{
    private static readonly Dictionary<string, string> Types = new()
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".map"] = "application/json",
    };

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ConcurrentDictionary<string, (byte[] Body, string Type)> _cache = new();

    public StaticSite(IAmazonS3 s3, IOptions<AppOptions> options)
    {
        _s3 = s3;
        _bucket = options.Value.WebBucketName;
    }

    public async Task<(int Status, byte[] Body, string Type)> ServeAsync(string path, CancellationToken ct = default)
    {
        var key = Normalize(path);
        var asset = await FetchAsync(key, ct) ?? await FetchAsync("index.html", ct);
        if (asset is null)
        {
            return (404, System.Text.Encoding.UTF8.GetBytes("Not found"), "text/plain");
        }
        return (200, asset.Value.Body, asset.Value.Type);
    }

    private static string Normalize(string path)
    {
        var clean = path.TrimStart('/');
        return clean.Length == 0 ? "index.html" : clean;
    }

    private static string ContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return Types.TryGetValue(ext, out var t) ? t : "application/octet-stream";
    }

    private async Task<(byte[] Body, string Type)?> FetchAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_bucket)) return null;
        // Vite emits content-hashed asset filenames, so they're safe to cache forever. index.html
        // keeps a stable name, so caching it would serve a stale SPA after a redeploy — always refetch.
        var cacheable = key != "index.html";
        if (cacheable && _cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            using var r = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, ct);
            using var ms = new MemoryStream();
            await r.ResponseStream.CopyToAsync(ms, ct);
            var asset = (ms.ToArray(), ContentType(key));
            if (cacheable) _cache[key] = asset;
            return asset;
        }
        catch (AmazonS3Exception)
        {
            return null;
        }
    }
}
