using Amazon.DynamoDBv2;
using Amazon.EventBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppClient.Core;
using WhatsAppClient.ReceiveLambda.Configuration;
using WhatsAppClient.ReceiveLambda.Events;
using WhatsAppClient.ReceiveLambda.Persistence;
using WhatsAppClient.ReceiveLambda.Processing;

namespace WhatsAppClient.ReceiveLambda;

public static class ReceiveServiceCollectionExtensions
{
    /// <summary>
    /// Registers the inbound pipeline: Core's WhatsApp messaging/media/parser services plus the
    /// DynamoDB store, EventBridge publisher, and the orchestrating processor. Binds
    /// <see cref="ReceiveOptions"/> from the "Receive" configuration section.
    /// </summary>
    public static IServiceCollection AddWhatsAppReceiver(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWhatsAppMessaging(configuration);

        services.AddOptions<ReceiveOptions>()
            .Bind(configuration.GetSection(ReceiveOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddAWSService<IAmazonDynamoDB>();
        services.TryAddAWSService<IAmazonEventBridge>();

        services.AddScoped<IInboundMessageStore, DynamoDbInboundMessageStore>();
        services.AddScoped<IInboundEventPublisher, EventBridgeInboundEventPublisher>();
        services.AddScoped<IInboundMessageProcessor, InboundMessageProcessor>();

        return services;
    }
}
