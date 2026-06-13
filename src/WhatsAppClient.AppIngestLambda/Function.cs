using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Ingest;
using WhatsAppClient.App.Persistence;

namespace WhatsAppClient.AppIngestLambda;

/// <summary>
/// EventBridge-triggered Lambda that projects inbound WhatsApp events into the app's single table
/// (contacts, conversations, messages). Rule: source whatsapp.inbound, detail-types
/// MessageReceived + StatusUpdated.
/// </summary>
public sealed class Function
{
    private readonly AppIngestProcessor _processor;

    public Function() : this(Build()) { }

    internal Function(AppIngestProcessor processor) => _processor = processor;

    public Task FunctionHandler(AppInboundEvent inboundEvent, ILambdaContext context) =>
        _processor.ProcessAsync(inboundEvent);

    private static AppIngestProcessor Build()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.SectionName));
        services.TryAddAWSService<IAmazonDynamoDB>();
        services.AddSingleton<IAppRepository, DynamoAppRepository>();
        services.AddSingleton<AppIngestProcessor>();

        return services.BuildServiceProvider().GetRequiredService<AppIngestProcessor>();
    }
}
