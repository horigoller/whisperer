using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppClient.Core.Services;
using WhatsAppClient.ReceiveLambda.Processing;

namespace WhatsAppClient.ReceiveLambda;

/// <summary>
/// Lambda that consumes inbound WhatsApp events from the SQS queue fed by the event-destination
/// SNS topic, parses them, and runs the receive pipeline (download media → persist → mark read →
/// fan out to EventBridge). Returns an <see cref="SQSBatchResponse"/> so only failed records are
/// retried (partial-batch responses).
/// </summary>
public sealed class Function
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly IWhatsAppEventParser? _parser;
    private readonly IInboundMessageProcessor? _processor;

    /// <summary>
    /// Used by the Lambda runtime. Builds the dependency graph from configuration
    /// (appsettings.json + environment variables) and the default AWS credential chain.
    /// </summary>
    public Function()
    {
        _serviceProvider = BuildServiceProvider();
    }

    /// <summary>Used by tests to inject the parser and processor directly.</summary>
    internal Function(IWhatsAppEventParser parser, IInboundMessageProcessor processor)
    {
        _parser = parser;
        _processor = processor;
    }

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        using var scope = _serviceProvider?.CreateScope();

        var parser = _parser ?? scope!.ServiceProvider.GetRequiredService<IWhatsAppEventParser>();
        var processor = _processor ?? scope!.ServiceProvider.GetRequiredService<IInboundMessageProcessor>();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var eventJson = ExtractEventJson(record.Body);
                var inboundEvent = parser.Parse(eventJson);
                await processor.ProcessAsync(inboundEvent, context.Logger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Reported as a batch item failure so SQS redelivers only this record (and
                // eventually routes it to the DLQ), leaving the rest of the batch acknowledged.
                context.Logger.LogError($"Failed to process SQS record {record.MessageId}: {ex}");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        return new SQSBatchResponse { BatchItemFailures = failures };
    }

    /// <summary>
    /// Returns the AWS event JSON regardless of whether the SNS→SQS subscription uses raw message
    /// delivery. Without raw delivery the SQS body is the SNS notification wrapper and the AWS
    /// event sits in its <c>Message</c> field; with raw delivery the body is the AWS event itself.
    /// </summary>
    internal static string ExtractEventJson(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("Type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "Notification"
                && root.TryGetProperty("Message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall through and return the raw body; the parser will surface a clear error.
        }

        return body;
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhatsAppReceiver(configuration);

        return services.BuildServiceProvider();
    }
}
