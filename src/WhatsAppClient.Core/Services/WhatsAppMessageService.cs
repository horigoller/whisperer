using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Validation;

namespace WhatsAppClient.Core.Services;

/// <inheritdoc />
public sealed class WhatsAppMessageService : IWhatsAppMessageService
{
    private const int MaxTextBodyLength = 4096;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAmazonSocialMessaging _client;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppMessageService> _logger;

    public WhatsAppMessageService(
        IAmazonSocialMessaging client,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppMessageService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> SendTextMessageAsync(
        string to,
        string body,
        bool previewUrl = false,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipient(to);

        if (string.IsNullOrEmpty(body))
        {
            throw new ArgumentException("Message body must not be empty.", nameof(body));
        }

        if (body.Length > MaxTextBodyLength)
        {
            throw new ArgumentException(
                $"Message body must not exceed {MaxTextBodyLength} characters.", nameof(body));
        }

        var message = new WhatsAppTextMessage
        {
            To = to,
            Text = new WhatsAppTextBody { Body = body, PreviewUrl = previewUrl },
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> SendTemplateMessageAsync(
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<WhatsAppTemplateComponent>? components = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipient(to);

        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name must not be empty.", nameof(templateName));
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ArgumentException("Language code must not be empty.", nameof(languageCode));
        }

        var message = new WhatsAppTemplateMessage
        {
            To = to,
            Template = new WhatsAppTemplate
            {
                Name = templateName,
                Language = new WhatsAppTemplateLanguage { Code = languageCode },
                Components = components,
            },
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> SendMessageAsync(
        WhatsAppMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateRecipient(message.To);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), SerializerOptions);
        return SendRawAsync(payload, $"{message.Type} message to {Mask(message.To)}", cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> SendMediaMessageAsync(
        string to,
        string mediaType,
        string? mediaId = null,
        string? link = null,
        string? caption = null,
        string? filename = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipient(to);

        if (string.IsNullOrWhiteSpace(mediaId) == string.IsNullOrWhiteSpace(link))
        {
            throw new ArgumentException("Provide exactly one of mediaId or link.", nameof(mediaId));
        }

        var media = new WhatsAppOutboundMedia { Id = mediaId, Link = link, Caption = caption, Filename = filename };

        WhatsAppMessage message = mediaType.ToLowerInvariant() switch
        {
            "image" => new WhatsAppImageMessage { To = to, Image = media },
            "document" => new WhatsAppDocumentMessage { To = to, Document = media },
            "video" => new WhatsAppVideoMessage { To = to, Video = media },
            "audio" => new WhatsAppAudioMessage { To = to, Audio = media },
            _ => throw new ArgumentException(
                $"Unsupported media type '{mediaType}'. Expected 'image', 'document', 'video', or 'audio'.",
                nameof(mediaType)),
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> MarkMessageReadAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message id must not be empty.", nameof(messageId));
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new WhatsAppReadReceipt { MessageId = messageId }, SerializerOptions);
        return SendRawAsync(payload, "read receipt", cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendWhatsAppMessageResult> SendReactionAsync(
        string to,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipient(to);

        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message id must not be empty.", nameof(messageId));
        }

        ArgumentNullException.ThrowIfNull(emoji);

        var message = new WhatsAppReactionMessage
        {
            To = to,
            Reaction = new WhatsAppReaction { MessageId = messageId, Emoji = emoji },
        };

        return SendMessageAsync(message, cancellationToken);
    }

    private async Task<SendWhatsAppMessageResult> SendRawAsync(
        byte[] payload,
        string description,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(payload);
        var request = new SendWhatsAppMessageRequest
        {
            OriginationPhoneNumberId = _options.OriginationPhoneNumberId,
            MetaApiVersion = _options.MetaApiVersion,
            Message = stream,
        };

        _logger.LogInformation("Sending WhatsApp {Description}", description);

        try
        {
            var response = await _client.SendWhatsAppMessageAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("WhatsApp message accepted with id {MessageId}", response.MessageId);
            return new SendWhatsAppMessageResult(response.MessageId);
        }
        catch (AmazonSocialMessagingException ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp {Description}", description);
            throw new WhatsAppMessageException($"Failed to send WhatsApp message: {ex.Message}", ex);
        }
    }

    private static void ValidateRecipient(string to)
    {
        if (!PhoneNumberValidator.IsValidE164(to))
        {
            throw new ArgumentException(
                $"'{to}' is not a valid E.164 phone number (expected format: +15551234567).", nameof(to));
        }
    }

    private static string Mask(string phoneNumber) =>
        phoneNumber.Length <= 4 ? "****" : $"***{phoneNumber[^4..]}";
}
