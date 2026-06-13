using System.Text.Json;
using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.Core.Tests.Services;

public class WhatsAppMessageReactionAndReadTests
{
    private readonly Mock<IAmazonSocialMessaging> _client = new();

    private WhatsAppMessageService CreateService()
    {
        var options = Options.Create(new WhatsAppOptions
        {
            OriginationPhoneNumberId = "phone-number-id-12345678901234567890123456789012",
            MetaApiVersion = "v21.0",
        });
        return new WhatsAppMessageService(_client.Object, options, NullLogger<WhatsAppMessageService>.Instance);
    }

    [Fact]
    public async Task MarkMessageReadAsync_SendsReadStatusPayload()
    {
        byte[]? bytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) => bytes = request.Message.ToArray())
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.ack" });

        await CreateService().MarkMessageReadAsync("wamid.incoming");

        using var doc = JsonDocument.Parse(bytes!);
        var root = doc.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("read", root.GetProperty("status").GetString());
        Assert.Equal("wamid.incoming", root.GetProperty("message_id").GetString());
        Assert.False(root.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task SendReactionAsync_SendsReactionPayload()
    {
        byte[]? bytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) => bytes = request.Message.ToArray())
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.ack" });

        await CreateService().SendReactionAsync("+15551234567", "wamid.incoming", "👋");

        using var doc = JsonDocument.Parse(bytes!);
        var root = doc.RootElement;
        Assert.Equal("+15551234567", root.GetProperty("to").GetString());
        Assert.Equal("reaction", root.GetProperty("type").GetString());
        var reaction = root.GetProperty("reaction");
        Assert.Equal("wamid.incoming", reaction.GetProperty("message_id").GetString());
        Assert.Equal("👋", reaction.GetProperty("emoji").GetString());
    }

    [Fact]
    public async Task MarkMessageReadAsync_WithEmptyMessageId_ThrowsAndDoesNotSend()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().MarkMessageReadAsync(""));
        _client.Verify(
            c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
