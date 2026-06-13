using Amazon.Lambda.Core;
using Microsoft.Extensions.Options;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;
using WhatsAppClient.ReceiveLambda.Configuration;
using WhatsAppClient.ReceiveLambda.Events;
using WhatsAppClient.ReceiveLambda.Persistence;

namespace WhatsAppClient.ReceiveLambda.Processing;

/// <inheritdoc />
public sealed class InboundMessageProcessor : IInboundMessageProcessor
{
    private readonly IInboundMessageStore _store;
    private readonly IInboundEventPublisher _publisher;
    private readonly IWhatsAppMessageService _messageService;
    private readonly IWhatsAppMediaService _mediaService;
    private readonly ReceiveOptions _options;

    public InboundMessageProcessor(
        IInboundMessageStore store,
        IInboundEventPublisher publisher,
        IWhatsAppMessageService messageService,
        IWhatsAppMediaService mediaService,
        IOptions<ReceiveOptions> options)
    {
        _store = store;
        _publisher = publisher;
        _messageService = messageService;
        _mediaService = mediaService;
        _options = options.Value;
    }

    public async Task ProcessAsync(
        WhatsAppInboundEvent inboundEvent,
        ILambdaLogger logger,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in inboundEvent.Messages)
        {
            await ProcessMessageAsync(inboundEvent, message, logger, cancellationToken).ConfigureAwait(false);
        }

        foreach (var status in inboundEvent.Statuses)
        {
            logger.LogInformation($"Status update for message {status.Id}: {status.Status}");
            await _store.SaveStatusAsync(inboundEvent, status, cancellationToken).ConfigureAwait(false);

            if (_options.PublishEvents)
            {
                await _publisher.PublishStatusAsync(inboundEvent, status, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessMessageAsync(
        WhatsAppInboundEvent inboundEvent,
        WhatsAppInboundMessage message,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"Received {message.Type} message {message.Id} from {message.From}");

        string? mediaS3Key = null;
        if (_options.DownloadMedia && message.Media?.Id is { } mediaId)
        {
            if (string.IsNullOrEmpty(_options.MediaBucketName))
            {
                logger.LogWarning("DownloadMedia is enabled but MediaBucketName is not configured; skipping download.");
            }
            else
            {
                mediaS3Key = BuildMediaKey(inboundEvent, message);
                var download = await _mediaService
                    .DownloadToS3Async(mediaId, _options.MediaBucketName, mediaS3Key, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation($"Downloaded media {mediaId} ({download.MimeType}, {download.FileSize} bytes) to {mediaS3Key}");
            }
        }

        await _store.SaveMessageAsync(inboundEvent, message, mediaS3Key, cancellationToken).ConfigureAwait(false);

        if (_options.MarkAsRead && message.Id is { } messageId)
        {
            await _messageService.MarkMessageReadAsync(messageId, cancellationToken).ConfigureAwait(false);
        }

        if (_options.PublishEvents)
        {
            await _publisher.PublishMessageAsync(inboundEvent, message, mediaS3Key, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildMediaKey(WhatsAppInboundEvent inboundEvent, WhatsAppInboundMessage message)
    {
        var name = message.Media?.Filename is { Length: > 0 } filename
            ? Sanitize(filename)
            : message.Media?.Id ?? "media";

        return $"{inboundEvent.PhoneNumberId}/{message.From}/{message.Id}/{name}";
    }

    private static string Sanitize(string value) =>
        string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}
