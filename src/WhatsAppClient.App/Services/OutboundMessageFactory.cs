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
        string to, string id, string name, string? languageCode, IReadOnlyList<string> bodyParams)
    {
        IReadOnlyList<WhatsAppTemplateComponent>? components = bodyParams.Count > 0
            ? new[]
            {
                new WhatsAppTemplateComponent
                {
                    Type = "body",
                    Parameters = bodyParams.Select(WhatsAppTemplateParameter.FromText).ToList(),
                },
            }
            : null;
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
