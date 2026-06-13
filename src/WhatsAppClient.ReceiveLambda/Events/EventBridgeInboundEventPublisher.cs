using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.ReceiveLambda.Configuration;

namespace WhatsAppClient.ReceiveLambda.Events;

/// <summary>
/// Publishes normalized inbound events to an EventBridge bus with <c>Source = "whatsapp.inbound"</c>
/// and <c>DetailType = "MessageReceived" | "StatusUpdated"</c>, so downstream rules can route them.
/// </summary>
public sealed class EventBridgeInboundEventPublisher : IInboundEventPublisher
{
    private const string EventSource = "whatsapp.inbound";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAmazonEventBridge _eventBridge;
    private readonly ReceiveOptions _options;

    public EventBridgeInboundEventPublisher(IAmazonEventBridge eventBridge, IOptions<ReceiveOptions> options)
    {
        _eventBridge = eventBridge;
        _options = options.Value;
    }

    public Task PublishMessageAsync(
        WhatsAppInboundEvent context,
        WhatsAppInboundMessage message,
        string? mediaS3Key,
        CancellationToken cancellationToken = default)
    {
        var detail = new
        {
            context.EventId,
            context.WabaId,
            context.PhoneNumberId,
            context.DisplayPhoneNumber,
            ContactName = context.ResolveContactName(message.From),
            MediaS3Key = mediaS3Key,
            Message = message,
        };

        return PublishAsync("MessageReceived", detail, cancellationToken);
    }

    public Task PublishStatusAsync(
        WhatsAppInboundEvent context,
        WhatsAppStatus status,
        CancellationToken cancellationToken = default)
    {
        var detail = new
        {
            context.EventId,
            context.WabaId,
            context.PhoneNumberId,
            Status = status,
        };

        return PublishAsync("StatusUpdated", detail, cancellationToken);
    }

    private Task PublishAsync(string detailType, object detail, CancellationToken cancellationToken)
    {
        var entry = new PutEventsRequestEntry
        {
            Source = EventSource,
            DetailType = detailType,
            EventBusName = _options.EventBusName,
            Detail = JsonSerializer.Serialize(detail, SerializerOptions),
        };

        return _eventBridge.PutEventsAsync(
            new PutEventsRequest { Entries = [entry] }, cancellationToken);
    }
}
