using System.Text.Json;
using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.Core.Tests.Services;

public class WhatsAppMessageServiceTests
{
    private const string OriginationPhoneNumberId = "phone-number-id-12345678901234567890123456789012";
    private const string MetaApiVersion = "v21.0";

    private readonly Mock<IAmazonSocialMessaging> _client = new();

    private WhatsAppMessageService CreateService()
    {
        var options = Options.Create(new WhatsAppOptions
        {
            OriginationPhoneNumberId = OriginationPhoneNumberId,
            MetaApiVersion = MetaApiVersion,
        });

        return new WhatsAppMessageService(_client.Object, options, NullLogger<WhatsAppMessageService>.Instance);
    }

    [Fact]
    public async Task SendTextMessageAsync_SendsExpectedRequest_AndReturnsMessageId()
    {
        SendWhatsAppMessageRequest? capturedRequest = null;
        byte[]? capturedMessageBytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) =>
            {
                capturedRequest = request;
                capturedMessageBytes = request.Message.ToArray();
            })
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.abc123" });

        var service = CreateService();

        var result = await service.SendTextMessageAsync("+15551234567", "Hello!", previewUrl: true);

        Assert.Equal("wamid.abc123", result.MessageId);

        Assert.NotNull(capturedRequest);
        Assert.Equal(OriginationPhoneNumberId, capturedRequest!.OriginationPhoneNumberId);
        Assert.Equal(MetaApiVersion, capturedRequest.MetaApiVersion);

        using var document = ParseMessage(capturedMessageBytes!);
        var root = document.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("+15551234567", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Hello!", root.GetProperty("text").GetProperty("body").GetString());
        Assert.True(root.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Theory]
    [InlineData("not-a-phone-number")]
    [InlineData("15551234567")]
    [InlineData("")]
    public async Task SendTextMessageAsync_WithInvalidRecipient_ThrowsArgumentException(string to)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendTextMessageAsync(to, "Hello!"));

        _client.Verify(
            c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendTextMessageAsync_WithEmptyBody_ThrowsArgumentException()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendTextMessageAsync("+15551234567", string.Empty));

        Assert.Equal("body", ex.ParamName);
    }

    [Fact]
    public async Task SendTextMessageAsync_WithBodyExceedingMaxLength_ThrowsArgumentException()
    {
        var service = CreateService();
        var tooLong = new string('a', 4097);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendTextMessageAsync("+15551234567", tooLong));

        Assert.Equal("body", ex.ParamName);
    }

    [Fact]
    public async Task SendTemplateMessageAsync_SendsExpectedRequest()
    {
        byte[]? capturedMessageBytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) => capturedMessageBytes = request.Message.ToArray())
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.def456" });

        var service = CreateService();

        var components = new[]
        {
            new WhatsAppTemplateComponent
            {
                Type = "body",
                Parameters = [WhatsAppTemplateParameter.FromText("Jane Doe")],
            },
        };

        var result = await service.SendTemplateMessageAsync("+15551234567", "order_confirmation", "en_US", components);

        Assert.Equal("wamid.def456", result.MessageId);

        using var document = ParseMessage(capturedMessageBytes!);
        var root = document.RootElement;
        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("order_confirmation", template.GetProperty("name").GetString());
        Assert.Equal("en_US", template.GetProperty("language").GetProperty("code").GetString());
        Assert.Equal("Jane Doe", template.GetProperty("components")[0].GetProperty("parameters")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(null, "en_US")]
    [InlineData("", "en_US")]
    [InlineData("order_confirmation", null)]
    [InlineData("order_confirmation", "")]
    public async Task SendTemplateMessageAsync_WithMissingNameOrLanguage_ThrowsArgumentException(
        string? templateName, string? languageCode)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendTemplateMessageAsync("+15551234567", templateName!, languageCode!));
    }

    [Fact]
    public async Task SendMessageAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SendMessageAsync(null!));
    }

    [Fact]
    public async Task SendMessageAsync_WhenAwsSdkThrows_WrapsInWhatsAppMessageException()
    {
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException("Invalid phone number id"));

        var service = CreateService();

        var message = new WhatsAppTextMessage
        {
            To = "+15551234567",
            Text = new WhatsAppTextBody { Body = "Hello!" },
        };

        var ex = await Assert.ThrowsAsync<WhatsAppMessageException>(() => service.SendMessageAsync(message));

        Assert.IsType<ValidationException>(ex.InnerException);
    }

    private static JsonDocument ParseMessage(byte[] messageBytes) => JsonDocument.Parse(messageBytes);
}
