using System.Text.Json;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.Core.Tests.Services;

public class WhatsAppEventParserTests
{
    private readonly WhatsAppEventParser _parser = new();

    private static string Envelope(string webhookEntryJson) => $$"""
    {
      "context": {
        "MetaWabaIds": [ { "wabaId": "1234567890abcde", "arn": "arn:aws:social-messaging:us-east-1:123456789012:waba/abc" } ],
        "MetaPhoneNumberIds": [ { "metaPhoneNumberId": "abcde1234567890", "arn": "arn:aws:social-messaging:us-east-1:123456789012:phone-number-id/xyz" } ]
      },
      "whatsAppWebhookEntry": {{JsonSerializer.Serialize(webhookEntryJson)}},
      "aws_account_id": "123456789012",
      "message_timestamp": "2025-01-08T23:30:43.271279391Z",
      "messageId": "6d69f07a-c317-4278-9d5c-6a84078419ec"
    }
    """;

    [Fact]
    public void Parse_IncomingTextMessage_FlattensMessageAndContact()
    {
        const string entry = """
        {
          "id": "365731266123456",
          "changes": [
            {
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "12065550100", "phone_number_id": "321010217712345" },
                "contacts": [ { "profile": { "name": "Diego" }, "wa_id": "12065550102" } ],
                "messages": [
                  { "from": "14255550150", "id": "wamid.TEXT", "timestamp": "1723506035", "text": { "body": "Hi" }, "type": "text" }
                ]
              },
              "field": "messages"
            }
          ]
        }
        """;

        var result = _parser.Parse(Envelope(entry));

        Assert.Equal("6d69f07a-c317-4278-9d5c-6a84078419ec", result.EventId);
        Assert.Equal("321010217712345", result.PhoneNumberId);
        Assert.Equal("12065550100", result.DisplayPhoneNumber);

        var message = Assert.Single(result.Messages);
        Assert.Equal("text", message.Type);
        Assert.Equal("14255550150", message.From);
        Assert.Equal("wamid.TEXT", message.Id);
        Assert.Equal("Hi", message.Text?.Body);
        Assert.Null(message.Media);
        Assert.Empty(result.Statuses);
        Assert.Equal("Diego", result.ResolveContactName("12065550102"));
    }

    [Fact]
    public void Parse_IncomingImageMessage_ExposesMediaInfo()
    {
        const string entry = """
        {
          "id": "365731266123456",
          "changes": [
            {
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "12065550100", "phone_number_id": "321010217760100" },
                "contacts": [ { "profile": { "name": "Diego" }, "wa_id": "12065550102" } ],
                "messages": [
                  {
                    "from": "14255550150",
                    "id": "wamid.IMAGE",
                    "timestamp": "1723506230",
                    "type": "image",
                    "image": { "mime_type": "image/jpeg", "sha256": "BTD0xlqSZ7l02o", "id": "530339869524171" }
                  }
                ]
              },
              "field": "messages"
            }
          ]
        }
        """;

        var result = _parser.Parse(Envelope(entry));

        var message = Assert.Single(result.Messages);
        Assert.Equal("image", message.Type);
        Assert.NotNull(message.Media);
        Assert.Equal("530339869524171", message.Media!.Id);
        Assert.Equal("image/jpeg", message.Media.MimeType);
    }

    [Fact]
    public void Parse_StatusUpdate_FlattensStatus()
    {
        const string entry = """
        {
          "id": "503131219501234",
          "changes": [
            {
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "14255550123", "phone_number_id": "46271669example" },
                "statuses": [
                  {
                    "id": "wamid.SENT",
                    "status": "sent",
                    "timestamp": "1736379042",
                    "recipient_id": "01234567890",
                    "conversation": { "id": "62374592", "origin": { "type": "utility" } },
                    "pricing": { "billable": true, "pricing_model": "CBP", "category": "utility" }
                  }
                ]
              },
              "field": "messages"
            }
          ]
        }
        """;

        var result = _parser.Parse(Envelope(entry));

        Assert.Empty(result.Messages);
        var status = Assert.Single(result.Statuses);
        Assert.Equal("wamid.SENT", status.Id);
        Assert.Equal("sent", status.Status);
        Assert.Equal("01234567890", status.RecipientId);
        Assert.Equal("utility", status.Pricing?.Category);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<JsonException>(() => _parser.Parse("{ not json"));
    }
}
