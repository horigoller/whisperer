using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;

namespace WhatsAppClient.App.Services;

public sealed record ApprovedTemplate(string Name, string? Language, string? Category);

public sealed class TemplateService
{
    private readonly IAmazonSocialMessaging _client;
    private readonly AppOptions _options;

    public TemplateService(IAmazonSocialMessaging client, IOptions<AppOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <summary>Approved templates for the "initiate utility exchange" picker.</summary>
    public async Task<IReadOnlyList<ApprovedTemplate>> ListApprovedAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.WabaId)) return [];

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

        return result;
    }
}
