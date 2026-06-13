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

public class WhatsAppMediaMessageTests
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
    public async Task SendMediaMessageAsync_Image_ById_SerializesExpectedShape()
    {
        byte[]? bytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) => bytes = request.Message.ToArray())
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.img" });

        await CreateService().SendMediaMessageAsync(
            "+15551234567", "image", mediaId: "media-123", caption: "A photo");

        using var doc = JsonDocument.Parse(bytes!);
        var root = doc.RootElement;
        Assert.Equal("+15551234567", root.GetProperty("to").GetString());
        Assert.Equal("image", root.GetProperty("type").GetString());
        var image = root.GetProperty("image");
        Assert.Equal("media-123", image.GetProperty("id").GetString());
        Assert.Equal("A photo", image.GetProperty("caption").GetString());
        Assert.False(image.TryGetProperty("link", out _)); // null link omitted
    }

    [Fact]
    public async Task SendMediaMessageAsync_Document_ByLink_SerializesLinkAndFilename()
    {
        byte[]? bytes = null;
        _client
            .Setup(c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendWhatsAppMessageRequest, CancellationToken>((request, _) => bytes = request.Message.ToArray())
            .ReturnsAsync(new SendWhatsAppMessageResponse { MessageId = "wamid.doc" });

        await CreateService().SendMediaMessageAsync(
            "+15551234567", "document", link: "https://example.com/f.pdf", filename: "f.pdf");

        using var doc = JsonDocument.Parse(bytes!);
        var document = doc.RootElement.GetProperty("document");
        Assert.Equal("document", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("https://example.com/f.pdf", document.GetProperty("link").GetString());
        Assert.Equal("f.pdf", document.GetProperty("filename").GetString());
    }

    [Theory]
    [InlineData(null, null)]                                   // neither
    [InlineData("media-1", "https://example.com/x.jpg")]        // both
    public async Task SendMediaMessageAsync_RequiresExactlyOneSource(string? mediaId, string? link)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().SendMediaMessageAsync("+15551234567", "image", mediaId: mediaId, link: link));

        _client.Verify(
            c => c.SendWhatsAppMessageAsync(It.IsAny<SendWhatsAppMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMediaMessageAsync_UnsupportedType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().SendMediaMessageAsync("+15551234567", "sticker", mediaId: "m"));
    }
}
