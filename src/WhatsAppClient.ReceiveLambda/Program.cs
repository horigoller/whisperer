using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using WhatsAppClient.ReceiveLambda;

var function = new Function();

await LambdaBootstrapBuilder.Create<SQSEvent, SQSBatchResponse>(
        function.FunctionHandler,
        new SourceGeneratorLambdaJsonSerializer<LambdaJsonSerializerContext>())
    .Build()
    .RunAsync();
