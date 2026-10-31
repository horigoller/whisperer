using WhatsAppClient.Core.Models;

namespace WhatsAppClient.App.Services;

/// <summary>Builds outbound WhatsApp message payloads shared across the domain services.</summary>
internal static class OutboundMessageFactory
{
    /// <summary>
    /// A template message with optional body parameters. <paramref name="id"/> is stamped as
    /// <c>biz_opaque_callback_data</c> so a later status update correlates back to it.
    /// </summary>
    public static WhatsAppTemplateMessage Template(
        string to, string id, string name, string? languageCode, IReadOnlyList<string> bodyParams,
        WhatsAppTemplateParameter? header = null,
        IReadOnlyList<(int Index, string Payload)>? quickReplyButtons = null)
    {
        var parts = new List<WhatsAppTemplateComponent>();
        if (header is not null)
            parts.Add(new WhatsAppTemplateComponent { Type = "header", Parameters = new[] { header } });
        if (bodyParams.Count > 0)
            parts.Add(new WhatsAppTemplateComponent
            {
                Type = "body",
                Parameters = bodyParams.Select(WhatsAppTemplateParameter.FromText).ToList(),
            });
        if (quickReplyButtons is not null)
            foreach (var (index, payload) in quickReplyButtons)
                parts.Add(new WhatsAppTemplateComponent
                {
                    Type = "button",
                    SubType = "quick_reply",
                    Index = index.ToString(),
                    Parameters = new[] { WhatsAppTemplateParameter.FromPayload(payload) },
                });
        IReadOnlyList<WhatsAppTemplateComponent>? components = parts.Count > 0 ? parts : null;
        return new WhatsAppTemplateMessage
        {
            To = to,
            BizOpaqueCallbackData = id,
            Template = new WhatsAppTemplate
            {
                Name = name,
                Language = new WhatsAppTemplateLanguage { Code = string.IsNullOrWhiteSpace(languageCode) ? "en_US" : languageCode },
                Components = components,
            },
        };
    }
}
