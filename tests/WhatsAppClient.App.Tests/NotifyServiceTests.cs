using Moq;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Realtime;
using WhatsAppClient.App.Services;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class NotifyServiceTests
{
    private readonly InMemoryAppRepository _repo = new();
    private readonly Mock<IWhatsAppMessageService> _whatsapp = new();
    private readonly Mock<IWhatsAppMediaService> _media = new();
    private readonly Mock<IOutboundMediaStore> _mediaStore = new();
    private readonly Mock<IRealtimePublisher> _realtime = new();
    private readonly Mock<IAlertImageComposer> _composer = new();
    private WhatsAppMessage? _sent;
    private IReadOnlyList<byte[]>? _composed;

    private NotifyService Svc()
    {
        _whatsapp
            .Setup(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppMessage, CancellationToken>((m, _) => _sent = m)
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.123"));
        _composer
            .Setup(c => c.Compose(It.IsAny<IReadOnlyList<byte[]>>()))
            .Callback<IReadOnlyList<byte[]>>(s => _composed = s)
            .Returns(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }); // stand-in composed JPEG
        return new NotifyService(_repo, _whatsapp.Object, _media.Object, _mediaStore.Object, _realtime.Object, _composer.Object);
    }

    // Notify only sends to known contacts, so success cases seed one.
    private void SeedContact(string waId = "17742625384") =>
        _repo.Contacts[waId] = new Contact { WaId = waId, PhoneE164 = "+" + waId, Name = "Home", Source = "manual", CreatedAt = "now" };

    [Fact]
    public async Task SendAsync_Text_SendsTextAndPersists()
    {
        SeedContact();
        var result = await Svc().SendAsync(new NotifyRequest("+1 (774) 262-5384", Text: "Garage door left open"));

        var text = Assert.IsType<WhatsAppTextMessage>(_sent);
        Assert.Equal("+17742625384", text.To);
        Assert.Equal("Garage door left open", text.Text.Body);
        Assert.False(string.IsNullOrEmpty(text.BizOpaqueCallbackData)); // correlatable
        Assert.Equal("text", result.Kind);
        Assert.Equal("17742625384", result.WaId);
        Assert.Equal("wamid.123", result.WaMessageId);

        Assert.Single(_repo.Messages["17742625384"]);                      // outbound persisted
        _realtime.Verify(r => r.PublishAsync(It.IsAny<RealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Template_SendsTemplateOutsideWindow()
    {
        SeedContact();
        var result = await Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_event", Params: new[] { "Garage door open" }));

        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        Assert.Equal("home_event", tmpl.Template.Name);
        Assert.Equal("en_US", tmpl.Template.Language.Code);
        Assert.Equal("Garage door open", tmpl.Template.Components!.Single(c => c.Type == "body").Parameters.Single().Text);
        Assert.False(string.IsNullOrEmpty(tmpl.BizOpaqueCallbackData));
        Assert.Equal("template", result.Kind);
        Assert.Equal("home_event", _repo.Messages["17742625384"][0].TemplateName);
    }

    [Fact]
    public async Task SendAsync_TemplateWithMediaUrl_AddsImageHeaderAndRenders()
    {
        SeedContact();
        var result = await Svc().SendAsync(new NotifyRequest("+17742625384",
            Template: "home_snapshot", Params: new[] { "Garage door open" },
            MediaUrl: "https://example.com/snap.jpg", MediaType: "image"));

        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        var header = tmpl.Template.Components!.Single(c => c.Type == "header").Parameters.Single();
        Assert.Equal("image", header.Type);
        Assert.Equal("https://example.com/snap.jpg", header.Image!.Link);
        Assert.Equal("Garage door open", tmpl.Template.Components!.Single(c => c.Type == "body").Parameters.Single().Text);
        Assert.Equal("template", result.Kind);

        var persisted = _repo.Messages["17742625384"][0];
        Assert.Equal("image", persisted.Type);                 // stored as image so the console renders it
        Assert.Equal("https://example.com/snap.jpg", persisted.MediaUrl);
        Assert.Equal("home_snapshot", persisted.TemplateName);
    }

    [Fact]
    public async Task SendAsync_TemplateWithHeaderText_AddsTextHeader()
    {
        SeedContact();
        await Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_event",
            HeaderText: "Wayland", Params: new[] { "Wayland", "Garage door closed" }));

        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        var header = tmpl.Template.Components!.Single(c => c.Type == "header").Parameters.Single();
        Assert.Equal("text", header.Type);
        Assert.Equal("Wayland", header.Text);
        var body = tmpl.Template.Components!.Single(c => c.Type == "body").Parameters;
        Assert.Equal(new[] { "Wayland", "Garage door closed" }, body.Select(p => p.Text));
    }

    [Fact]
    public async Task SendAsync_TemplateWithSnapshots_ComposesBrandedImageHeader()
    {
        SeedContact();
        _mediaStore.Setup(s => s.StageAsync(It.IsAny<byte[]>(), "image", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("media-bucket", "outgoing/notify/alert.jpg"));
        _media.Setup(m => m.UploadFromS3Async("media-bucket", "outgoing/notify/alert.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync("ALERT_HANDLE");

        var snapA = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var snapB = Convert.ToBase64String(new byte[] { 5, 6, 7, 8 });
        await Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_snapshot",
            Params: new[] { "Wayland", "Garage door open" }, Snapshots: new[] { snapA, snapB }));

        Assert.Equal(2, _composed!.Count);                       // both snapshots decoded and composed
        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        var header = tmpl.Template.Components!.Single(c => c.Type == "header").Parameters.Single();
        Assert.Equal("image", header.Type);
        Assert.Equal("ALERT_HANDLE", header.Image!.Id);          // composed image uploaded → handle in header
        var bodyParams = tmpl.Template.Components!.Single(c => c.Type == "body").Parameters;
        Assert.Equal(new[] { "Wayland", "Garage door open" }, bodyParams.Select(p => p.Text));

        var persisted = _repo.Messages["17742625384"][0];
        Assert.Equal("image", persisted.Type);                   // stored as image so the console renders it
        Assert.Equal("outgoing/notify/alert.jpg", persisted.MediaS3Key);
        Assert.Equal("home_snapshot", persisted.TemplateName);
    }

    [Fact]
    public async Task SendAsync_TemplateWithEmptySnapshots_ComposesBannerOnly()
    {
        SeedContact();
        _mediaStore.Setup(s => s.StageAsync(It.IsAny<byte[]>(), "image", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("media-bucket", "outgoing/notify/alert.jpg"));
        _media.Setup(m => m.UploadFromS3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ALERT_HANDLE");

        await Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_snapshot",
            Params: new[] { "Wayland", "Door closed" }, Snapshots: Array.Empty<string>()));

        Assert.Empty(_composed!);                                // banner-only: composer still runs, no tiles
        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        var header = tmpl.Template.Components!.Single(c => c.Type == "header").Parameters.Single();
        Assert.Equal("image", header.Type);
        Assert.Equal("ALERT_HANDLE", header.Image!.Id);
    }

    [Fact]
    public async Task SendAsync_Actionable_AddsTwoQuickReplyButtonsAndStoresBinding()
    {
        SeedContact();
        _mediaStore.Setup(s => s.StageAsync(It.IsAny<byte[]>(), "image", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("media-bucket", "outgoing/notify/alert.jpg"));
        _media.Setup(m => m.UploadFromS3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ALERT_HANDLE");

        var before = DateTimeOffset.UtcNow;
        var result = await Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_action",
            Params: new[] { "Wayland", "Garage door open" },
            CallbackUrl: "https://ha.example.com/api/webhook/abc", ActionData: "close_garage"));

        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        Assert.Equal("image", tmpl.Template.Components!.Single(c => c.Type == "header").Parameters.Single().Type);
        var buttons = tmpl.Template.Components!.Where(c => c.Type == "button").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal("quick_reply", buttons[0].SubType);
        Assert.Equal("0", buttons[0].Index);
        Assert.Equal($"H:{result.MessageId}", buttons[0].Parameters.Single().Payload);
        Assert.Equal("1", buttons[1].Index);
        Assert.Equal($"I:{result.MessageId}", buttons[1].Parameters.Single().Payload);

        var binding = _repo.Bindings[result.MessageId];
        Assert.Equal("active", binding.Status);
        Assert.Equal("https://ha.example.com/api/webhook/abc", binding.CallbackUrl);
        Assert.Equal("close_garage", binding.Data);
        Assert.Equal("Wayland", binding.Home);
        Assert.Equal("17742625384", binding.WaId);
        // Expiry ~5 minutes out.
        Assert.InRange(binding.ExpiresAt, before.AddMinutes(4).ToUnixTimeSeconds(), before.AddMinutes(6).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task SendAsync_CallbackUrlWithoutTemplate_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Text: "hi",
                CallbackUrl: "https://ha.example.com/api/webhook/abc")));
    }

    [Fact]
    public async Task SendAsync_NonHttpsCallbackUrl_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_action",
                Params: new[] { "Wayland", "x" }, CallbackUrl: "http://ha.example.com/api/webhook/abc")));
    }

    [Fact]
    public async Task SendAsync_SnapshotsWithoutTemplate_Throws()
    {
        SeedContact();
        var snap = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Text: "hi", Snapshots: new[] { snap })));
    }

    [Fact]
    public async Task SendAsync_InvalidSnapshotBase64_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_snapshot",
                Params: new[] { "Wayland", "x" }, Snapshots: new[] { "!!!notbase64!!!" })));
    }

    [Fact]
    public async Task SendAsync_TemplateBlankParam_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Template: "home_event", Params: new[] { "ok", "  " })));
    }

    [Fact]
    public async Task SendAsync_UnknownContact_ThrowsAndDoesNotSend()
    {
        await Assert.ThrowsAsync<ContactNotFoundException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", Text: "hi")));
        _whatsapp.Verify(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_MediaUrl_SendsImageWithCaption_NoUpload()
    {
        SeedContact();
        await Svc().SendAsync(new NotifyRequest("+17742625384",
            MediaUrl: "https://example.com/snap.jpg", MediaType: "image", Caption: "Front door"));

        var img = Assert.IsType<WhatsAppImageMessage>(_sent);
        Assert.Equal("https://example.com/snap.jpg", img.Image.Link);
        Assert.Null(img.Image.Id);
        Assert.Equal("Front door", img.Image.Caption);
        var persisted = _repo.Messages["17742625384"][0];
        Assert.Equal("https://example.com/snap.jpg", persisted.MediaUrl);  // remote link kept for the console
        Assert.Null(persisted.MediaS3Key);                                 // not staged in S3
        _mediaStore.Verify(s => s.StageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _media.Verify(m => m.UploadFromS3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_MediaBase64Video_StagesUploadsAndSendsHandle()
    {
        _mediaStore.Setup(s => s.StageAsync(It.IsAny<byte[]>(), "video", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("media-bucket", "outgoing/notify/abc.mp4"));
        _media.Setup(m => m.UploadFromS3Async("media-bucket", "outgoing/notify/abc.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync("MEDIA_HANDLE");
        SeedContact();

        var b64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        await Svc().SendAsync(new NotifyRequest("+17742625384", MediaBase64: b64, MediaType: "video", Text: "Motion detected"));

        var vid = Assert.IsType<WhatsAppVideoMessage>(_sent);
        Assert.Equal("MEDIA_HANDLE", vid.Video.Id);
        Assert.Null(vid.Video.Link);
        Assert.Equal("Motion detected", vid.Video.Caption); // text used as caption when no explicit caption
        var persisted = _repo.Messages["17742625384"][0];
        Assert.Equal("Motion detected", persisted.Text);                  // console preview mirrors the caption
        Assert.Equal("outgoing/notify/abc.mp4", persisted.MediaS3Key);    // staged key kept so the console can render it
    }

    [Fact]
    public async Task SendAsync_MediaWithoutMediaType_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", MediaUrl: "https://example.com/y.jpg")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a phone")]
    public async Task SendAsync_BadPhone_Throws(string? to)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().SendAsync(new NotifyRequest(to, Text: "hi")));
    }

    [Fact]
    public async Task SendAsync_NoPayload_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().SendAsync(new NotifyRequest("+17742625384")));
    }

    [Fact]
    public async Task SendAsync_BadMediaType_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", MediaUrl: "https://x/y.bin", MediaType: "audio")));
    }

    [Fact]
    public async Task SendAsync_InvalidBase64_Throws()
    {
        SeedContact();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc().SendAsync(new NotifyRequest("+17742625384", MediaBase64: "!!!notbase64!!!", MediaType: "image")));
    }
}
