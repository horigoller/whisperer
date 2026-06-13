using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Moq;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;
using WhatsAppClient.ReceiveLambda;
using WhatsAppClient.ReceiveLambda.Processing;
using Xunit;

namespace WhatsAppClient.ReceiveLambda.Tests;

public class FunctionTests
{
    private const string TextEntry = """
    {
      "id": "365731266123456",
      "changes": [
        {
          "value": {
            "messaging_product": "whatsapp",
            "metadata": { "display_phone_number": "12065550100", "phone_number_id": "pn-1" },
            "contacts": [ { "profile": { "name": "Diego" }, "wa_id": "12065550102" } ],
            "messages": [ { "from": "14255550150", "id": "wamid.TEXT", "timestamp": "1723506035", "text": { "body": "Hi" }, "type": "text" } ]
          },
          "field": "messages"
        }
      ]
    }
    """;

    private readonly Mock<IInboundMessageProcessor> _processor = new();
    private readonly TestLambdaContext _context = new();

    // Uses the real parser so the test also exercises envelope/entry decoding.
    private Function CreateFunction() => new(new WhatsAppEventParser(), _processor.Object);

    private static string Envelope(string entryJson) => $$"""
    { "whatsAppWebhookEntry": {{System.Text.Json.JsonSerializer.Serialize(entryJson)}}, "messageId": "evt-1" }
    """;

    private static SQSEvent.SQSMessage Record(string id, string body) => new() { MessageId = id, Body = body };

    [Fact]
    public async Task FunctionHandler_ParsesAndProcessesEachRecord_NoFailures()
    {
        WhatsAppInboundEvent? processed = null;
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<WhatsAppInboundEvent>(), It.IsAny<Amazon.Lambda.Core.ILambdaLogger>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppInboundEvent, Amazon.Lambda.Core.ILambdaLogger, CancellationToken>((evt, _, _) => processed = evt)
            .Returns(Task.CompletedTask);

        var sqsEvent = new SQSEvent { Records = [Record("r1", Envelope(TextEntry))] };

        var response = await CreateFunction().FunctionHandler(sqsEvent, _context);

        Assert.Empty(response.BatchItemFailures);
        Assert.NotNull(processed);
        var message = Assert.Single(processed!.Messages);
        Assert.Equal("Hi", message.Text?.Body);
    }

    [Fact]
    public async Task FunctionHandler_WhenProcessingThrows_ReportsBatchItemFailureForThatRecordOnly()
    {
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<WhatsAppInboundEvent>(), It.IsAny<Amazon.Lambda.Core.ILambdaLogger>(), It.IsAny<CancellationToken>()))
            .Returns<WhatsAppInboundEvent, Amazon.Lambda.Core.ILambdaLogger, CancellationToken>((evt, _, _) =>
                evt.EventId == "evt-bad" ? throw new InvalidOperationException("downstream failure") : Task.CompletedTask);

        var good = Record("good", """{ "whatsAppWebhookEntry": "{}", "messageId": "evt-ok" }""");
        var bad = Record("bad", """{ "whatsAppWebhookEntry": "{}", "messageId": "evt-bad" }""");
        var sqsEvent = new SQSEvent { Records = [good, bad] };

        var response = await CreateFunction().FunctionHandler(sqsEvent, _context);

        var failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("bad", failure.ItemIdentifier);
    }

    [Fact]
    public void ExtractEventJson_UnwrapsSnsNotificationEnvelope()
    {
        const string awsEvent = """{ "messageId": "evt-1" }""";
        var snsBody = $$"""{ "Type": "Notification", "MessageId": "x", "Message": {{System.Text.Json.JsonSerializer.Serialize(awsEvent)}} }""";

        Assert.Equal(awsEvent, Function.ExtractEventJson(snsBody));
    }

    [Fact]
    public void ExtractEventJson_ReturnsBodyWhenRawDelivery()
    {
        const string rawBody = """{ "messageId": "evt-1", "whatsAppWebhookEntry": "{}" }""";

        Assert.Equal(rawBody, Function.ExtractEventJson(rawBody));
    }
}
