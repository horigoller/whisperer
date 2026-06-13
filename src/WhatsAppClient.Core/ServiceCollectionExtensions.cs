using Amazon.SocialMessaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        // Core depends on ILogger<T> but does not own logging configuration. Register a no-op
        // fallback so a minimal host (e.g. the send Lambda) resolves the services without calling
        // AddLogging; a host that does call AddLogging registers ILogger<> first, so this is a no-op there.
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.TryAddAWSService<IAmazonSocialMessaging>();
        services.AddScoped<IWhatsAppMessageService, WhatsAppMessageService>();
        services.AddScoped<IWhatsAppMediaService, WhatsAppMediaService>();
        services.AddSingleton<IWhatsAppEventParser, WhatsAppEventParser>();

        return services;
    }
}
