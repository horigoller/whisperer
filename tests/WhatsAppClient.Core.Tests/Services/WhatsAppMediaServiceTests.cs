using Amazon.SocialMessaging;
using Amazon.SocialMessaging.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppClient.Core.Configuration;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.Core.Tests.Services;

public class WhatsAppMediaServiceTests
{
    private const string OriginationPhoneNumberId = "phone-number-id-12345678901234567890123456789012";

    private readonly Mock<IAmazonSocialMessaging> _client = new();

    private WhatsAppMediaService CreateService()
    {
        var options = Options.Create(new WhatsAppOptions
        {
            OriginationPhoneNumberId = OriginationPhoneNumberId,
            MetaApiVersion = "v21.0",
        });
        return new WhatsAppMediaService(_client.Object, options, NullLogger<WhatsAppMediaService>.Instance);
    }

    [Fact]
    public async Task DownloadToS3Async_SendsExpectedRequest_AndReturnsMetadata()
    {
        GetWhatsAppMessageMediaRequest? captured = null;
        _client
            .Setup(c => c.GetWhatsAppMessageMediaAsync(It.IsAny<GetWhatsAppMessageMediaRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetWhatsAppMessageMediaRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetWhatsAppMessageMediaResponse { MimeType = "image/jpeg", FileSize = 2048 });

        var result = await CreateService().DownloadToS3Async("media-1", "my-bucket", "incoming/file.jpg");

        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(2048, result.FileSize);

        Assert.NotNull(captured);
        Assert.Equal("media-1", captured!.MediaId);
        Assert.Equal(OriginationPhoneNumberId, captured.OriginationPhoneNumberId);
        Assert.Equal("my-bucket", captured.DestinationS3File.BucketName);
        Assert.Equal("incoming/file.jpg", captured.DestinationS3File.Key);
    }

    [Fact]
    public async Task UploadFromS3Async_SendsExpectedRequest_AndReturnsMediaId()
    {
        PostWhatsAppMessageMediaRequest? captured = null;
        _client
            .Setup(c => c.PostWhatsAppMessageMediaAsync(It.IsAny<PostWhatsAppMessageMediaRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostWhatsAppMessageMediaRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PostWhatsAppMessageMediaResponse { MediaId = "uploadedmediaid" });

        var mediaId = await CreateService().UploadFromS3Async("my-bucket", "outgoing/file.jpg");

        Assert.Equal("uploadedmediaid", mediaId);
        Assert.NotNull(captured);
        Assert.Equal(OriginationPhoneNumberId, captured!.OriginationPhoneNumberId);
        Assert.Equal("my-bucket", captured.SourceS3File.BucketName);
        Assert.Equal("outgoing/file.jpg", captured.SourceS3File.Key);
    }

    [Fact]
    public async Task DownloadToS3Async_WhenAwsThrows_WrapsInWhatsAppMessageException()
    {
        _client
            .Setup(c => c.GetWhatsAppMessageMediaAsync(It.IsAny<GetWhatsAppMessageMediaRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSocialMessagingException("boom"));

        await Assert.ThrowsAsync<WhatsAppMessageException>(
            () => CreateService().DownloadToS3Async("media-1", "my-bucket", "key"));
    }
}
