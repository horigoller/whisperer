using Amazon.DynamoDBv2;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Services;
using WhatsAppClient.Core;

namespace WhatsAppClient.App;

public static class AppServiceCollectionExtensions
{
    /// <summary>
    /// Registers the management-app services: Core WhatsApp messaging, the DynamoDB repository,
    /// auth (codes + JWT), and the domain services. Binds <see cref="AppOptions"/> from "App".
    /// </summary>
    public static IServiceCollection AddWhatsAppApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWhatsAppMessaging(configuration);

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddAWSService<IAmazonDynamoDB>();
        services.TryAddAWSService<IAmazonSecretsManager>();

        services.AddSingleton<IAppRepository, DynamoAppRepository>();
        services.AddSingleton<ISessionSecretProvider, SessionSecretProvider>();
        services.AddSingleton<ISessionTokenService, SessionTokenService>();

        services.AddScoped<AuthService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<ContactService>();
        services.AddScoped<UserService>();
        services.AddScoped<TemplateService>();

        return services;
    }
}
