using Amazon.SocialMessaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWhatsAppMessageService"/> and the underlying AWS End User
    /// Messaging Social client. Binds <see cref="WhatsAppOptions"/> from the
    /// "WhatsApp" configuration section.
    /// </summary>
    public static IServiceCollection AddWhatsAppMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection(WhatsAppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddAWSService<IAmazonSocialMessaging>();
        services.AddScoped<IWhatsAppMessageService, WhatsAppMessageService>();

        return services;
    }
}
