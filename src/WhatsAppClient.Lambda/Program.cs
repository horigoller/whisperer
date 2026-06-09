using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using WhatsAppClient.Lambda;
using WhatsAppClient.Lambda.Models;

var function = new Function();

await LambdaBootstrapBuilder.Create<SendMessageInput, SendMessageOutput>(
        function.FunctionHandler,
        new SourceGeneratorLambdaJsonSerializer<LambdaJsonSerializerContext>())
    .Build()
    .RunAsync();
