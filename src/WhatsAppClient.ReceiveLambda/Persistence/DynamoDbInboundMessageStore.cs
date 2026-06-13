using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.ReceiveLambda.Configuration;

namespace WhatsAppClient.ReceiveLambda.Persistence;

/// <summary>
/// Stores inbound messages and statuses in a single DynamoDB table. Items are keyed by
/// <c>PK = "WA#{waId}"</c> and <c>SK = "MSG#{ts}#{id}"</c> (or <c>"STATUS#..."</c>) so a customer's
/// conversation reads back in chronological order via Query. A <c>MessageId</c> GSI allows
/// correlating a status with its original message once outbound messages are also persisted.
/// </summary>
public sealed class DynamoDbInboundMessageStore : IInboundMessageStore
{
    public const string MessageIdIndexName = "MessageId-index";

    private readonly IAmazonDynamoDB _dynamo;
    private readonly ReceiveOptions _options;

    public DynamoDbInboundMessageStore(IAmazonDynamoDB dynamo, IOptions<ReceiveOptions> options)
    {
        _dynamo = dynamo;
        _options = options.Value;
    }

    public Task SaveMessageAsync(
        WhatsAppInboundEvent context,
        WhatsAppInboundMessage message,
        string? mediaS3Key,
        CancellationToken cancellationToken = default)
    {
        var timestamp = message.Timestamp ?? string.Empty;
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S($"WA#{message.From}"),
            ["SK"] = S($"MSG#{timestamp}#{message.Id}"),
            ["Direction"] = S("inbound"),
        };

        Put(item, "MessageId", message.Id);
        Put(item, "Type", message.Type);
        Put(item, "From", message.From);
        Put(item, "ContactName", context.ResolveContactName(message.From));
        Put(item, "PhoneNumberId", context.PhoneNumberId);
        Put(item, "DisplayPhoneNumber", context.DisplayPhoneNumber);
        Put(item, "Timestamp", timestamp);
        Put(item, "EventId", context.EventId);
        Put(item, "Body", message.Text?.Body);
        Put(item, "MediaId", message.Media?.Id);
        Put(item, "MediaMimeType", message.Media?.MimeType);
        Put(item, "Caption", message.Media?.Caption);
        Put(item, "Filename", message.Media?.Filename);
        Put(item, "MediaS3Key", mediaS3Key);
        Put(item, "ReactionEmoji", message.Reaction?.Emoji);
        Put(item, "ReactionToMessageId", message.Reaction?.MessageId);
        Put(item, "ReplyToMessageId", message.Context?.Id);

        return _dynamo.PutItemAsync(
            new PutItemRequest { TableName = _options.MessagesTableName, Item = item }, cancellationToken);
    }

    public Task SaveStatusAsync(
        WhatsAppInboundEvent context,
        WhatsAppStatus status,
        CancellationToken cancellationToken = default)
    {
        var timestamp = status.Timestamp ?? string.Empty;
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S($"WA#{status.RecipientId}"),
            ["SK"] = S($"STATUS#{timestamp}#{status.Id}#{status.Status}"),
            ["Direction"] = S("status"),
        };

        Put(item, "MessageId", status.Id);
        Put(item, "Status", status.Status);
        Put(item, "RecipientId", status.RecipientId);
        Put(item, "Timestamp", timestamp);
        Put(item, "EventId", context.EventId);
        Put(item, "ConversationId", status.Conversation?.Id);
        Put(item, "PricingCategory", status.Pricing?.Category);

        var firstError = status.Errors?.FirstOrDefault();
        Put(item, "ErrorTitle", firstError?.Title);
        if (firstError?.Code is { } code)
        {
            item["ErrorCode"] = new AttributeValue { N = code.ToString() };
        }

        return _dynamo.PutItemAsync(
            new PutItemRequest { TableName = _options.MessagesTableName, Item = item }, cancellationToken);
    }

    private static AttributeValue S(string value) => new() { S = value };

    private static void Put(Dictionary<string, AttributeValue> item, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            item[name] = new AttributeValue { S = value };
        }
    }
}
