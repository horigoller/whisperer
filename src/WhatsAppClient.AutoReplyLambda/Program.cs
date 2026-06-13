using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using WhatsAppClient.AutoReplyLambda;
using WhatsAppClient.AutoReplyLambda.Models;

var function = new Function();

await LambdaBootstrapBuilder.Create<AutoReplyEvent, AutoReplyResult>(
        function.FunctionHandler,
        new SourceGeneratorLambdaJsonSerializer<LambdaJsonSerializerContext>())
    .Build()
    .RunAsync();
