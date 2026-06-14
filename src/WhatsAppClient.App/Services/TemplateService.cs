using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;

namespace WhatsAppClient.App.Services;

public sealed record ApprovedTemplate(string Name, string? Language, string? Category);

public interface ITemplateService
{
    Task<IReadOnlyList<ApprovedTemplate>> ListApprovedAsync(CancellationToken ct = default);
    Task<bool> IsApprovedAsync(string name, CancellationToken ct = default);
}

public sealed class TemplateService : ITemplateService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static volatile CacheEntry? _cache; // shared across the container's scoped instances

    private sealed record CacheEntry(DateTimeOffset At, IReadOnlyList<ApprovedTemplate> Templates);

    private readonly IAmazonSocialMessaging _client;
    private readonly AppOptions _options;

    public TemplateService(IAmazonSocialMessaging client, IOptions<AppOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <summary>Approved templates for the "initiate utility exchange" picker (cached ~60s).</summary>
    public async Task<IReadOnlyList<ApprovedTemplate>> ListApprovedAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.WabaId)) return [];

        var cached = _cache;
        if (cached is not null && DateTimeOffset.UtcNow - cached.At < CacheTtl) return cached.Templates;

        var result = new List<ApprovedTemplate>();
        string? nextToken = null;
        do
        {
            var r = await _client.ListWhatsAppMessageTemplatesAsync(
                new ListWhatsAppMessageTemplatesRequest { Id = _options.WabaId, NextToken = nextToken }, ct);
            foreach (var t in r.Templates ?? [])
            {
                if (t.TemplateStatus == "APPROVED" && !string.IsNullOrEmpty(t.TemplateName))
                {
                    result.Add(new ApprovedTemplate(t.TemplateName, t.TemplateLanguage, t.TemplateCategory));
                }
            }
            nextToken = r.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        _cache = new CacheEntry(DateTimeOffset.UtcNow, result);
        return result;
    }

    public async Task<bool> IsApprovedAsync(string name, CancellationToken ct = default)
    {
        var approved = await ListApprovedAsync(ct);
        return approved.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
