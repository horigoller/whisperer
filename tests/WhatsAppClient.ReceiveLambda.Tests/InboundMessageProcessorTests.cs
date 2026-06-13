using Amazon.Lambda.TestUtilities;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;
using WhatsAppClient.ReceiveLambda.Configuration;
using WhatsAppClient.ReceiveLambda.Events;
using WhatsAppClient.ReceiveLambda.Persistence;
using WhatsAppClient.ReceiveLambda.Processing;
using Xunit;

namespace WhatsAppClient.ReceiveLambda.Tests;

public class InboundMessageProcessorTests
{
    private readonly Mock<IInboundMessageStore> _store = new();
    private readonly Mock<IInboundEventPublisher> _publisher = new();
    private readonly Mock<IWhatsAppMessageService> _messageService = new();
    private readonly Mock<IWhatsAppMediaService> _mediaService = new();
    private readonly TestLambdaContext _context = new();

    private InboundMessageProcessor CreateProcessor(ReceiveOptions options)
    {
        _messageService
            .Setup(s => s.MarkMessageReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.ack"));
        _mediaService
            .Setup(m => m.DownloadToS3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppMediaDownloadResult("image/jpeg", 1024));

        return new InboundMessageProcessor(
            _store.Object, _publisher.Object, _messageService.Object, _mediaService.Object, Options.Create(options));
    }

    private static ReceiveOptions MakeOptions(bool markAsRead = true, bool downloadMedia = true, bool publish = true) => new()
    {
        MessagesTableName = "messages",
        MediaBucketName = "media-bucket",
        EventBusName = "bus",
        MarkAsRead = markAsRead,
        DownloadMedia = downloadMedia,
        PublishEvents = publish,
    };

    private static WhatsAppInboundEvent EventWith(params WhatsAppInboundMessage[] messages) => new()
    {
        EventId = "evt-1",
        PhoneNumberId = "pn-1",
        Messages = messages,
    };

    [Fact]
    public async Task ProcessAsync_TextMessage_PersistsMarksReadAndPublishes()
    {
        var message = new WhatsAppInboundMessage
        {
            From = "14255550150",
            Id = "wamid.TEXT",
            Type = "text",
            Text = new WhatsAppInboundText { Body = "Hi" },
        };

        await CreateProcessor(MakeOptions()).ProcessAsync(EventWith(message), _context.Logger);

        _store.Verify(s => s.SaveMessageAsync(It.IsAny<WhatsAppInboundEvent>(), message, null, It.IsAny<CancellationToken>()), Times.Once);
        _messageService.Verify(s => s.MarkMessageReadAsync("wamid.TEXT", It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishMessageAsync(It.IsAny<WhatsAppInboundEvent>(), message, null, It.IsAny<CancellationToken>()), Times.Once);
        _mediaService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ImageMessage_DownloadsMediaAndStoresKey()
    {
        var message = new WhatsAppInboundMessage
        {
            From = "14255550150",
            Id = "wamid.IMAGE",
            Type = "image",
            Image = new WhatsAppMediaInfo { Id = "media-1", MimeType = "image/jpeg" },
        };

        await CreateProcessor(MakeOptions()).ProcessAsync(EventWith(message), _context.Logger);

        _mediaService.Verify(m => m.DownloadToS3Async("media-1", "media-bucket", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(
            s => s.SaveMessageAsync(It.IsAny<WhatsAppInboundEvent>(), message, It.Is<string?>(k => !string.IsNullOrEmpty(k)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MarkAsReadDisabled_DoesNotMarkRead()
    {
        var message = new WhatsAppInboundMessage { From = "x", Id = "wamid.1", Type = "text" };

        await CreateProcessor(MakeOptions(markAsRead: false)).ProcessAsync(EventWith(message), _context.Logger);

        _messageService.Verify(s => s.MarkMessageReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_Status_PersistsAndPublishes()
    {
        var status = new WhatsAppStatus { Id = "wamid.SENT", Status = "delivered", RecipientId = "14255550150" };
        var inboundEvent = new WhatsAppInboundEvent { EventId = "evt-1", PhoneNumberId = "pn-1", Statuses = [status] };

        await CreateProcessor(MakeOptions()).ProcessAsync(inboundEvent, _context.Logger);

        _store.Verify(s => s.SaveStatusAsync(inboundEvent, status, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishStatusAsync(inboundEvent, status, It.IsAny<CancellationToken>()), Times.Once);
    }
}
