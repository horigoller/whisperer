using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using WhatsAppClient.App.Ingest;
using WhatsAppClient.AppIngestLambda;

var function = new Function();

await LambdaBootstrapBuilder.Create<AppInboundEvent>(
        function.FunctionHandler,
        new SourceGeneratorLambdaJsonSerializer<LambdaJsonSerializerContext>())
    .Build()
    .RunAsync();
