using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WhatsAppClient.App.Ingest;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Realtime;
using WhatsAppClient.App.Services;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class AppIngestProcessorTests
{
    private readonly InMemoryAppRepository _repo = new();
    private readonly Mock<IRealtimePublisher> _realtime = new();
    private readonly Mock<IWhatsAppMessageService> _whatsapp = new();
    private readonly Mock<IHaCallbackClient> _ha = new();
    private AppIngestProcessor Processor() =>
        new(_repo, _realtime.Object, _whatsapp.Object, _ha.Object, NullLogger<AppIngestProcessor>.Instance);

    private static AppInboundEvent ButtonTap(string payload, string? alertWamid = "wamid.ALERT") => new()
    {
        DetailType = "MessageReceived",
        Detail = new AppInboundDetail
        {
            Message = new WhatsAppInboundMessage
            {
                From = "17742625384", Id = "wamid.BTN", Timestamp = "1700000000", Type = "button",
                Button = new WhatsAppInboundButton { Text = "Handle It", Payload = payload },
                Context = alertWamid is null ? null : new WhatsAppMessageContext { Id = alertWamid },
            },
        },
    };

    private ButtonBinding SeedBinding(string token = "tok1", long? expiresAt = null, string status = "active") =>
        _repo.Bindings[token] = new ButtonBinding
        {
            Token = token, WaId = "17742625384", Home = "Wayland",
            CallbackUrl = "https://ha.example.com/api/webhook/abc", Data = "open_garage",
            Status = status,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            Ttl = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
        };

    [Fact]
    public async Task MessageReceived_SelfRegistersContact_StoresMessage_OpensWindowFromMessageTime()
    {
        var sentAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var evt = new AppInboundEvent
        {
            DetailType = "MessageReceived",
            Detail = new AppInboundDetail
            {
                ContactName = "Diego",
                Message = new WhatsAppInboundMessage
                {
                    From = "15551239999", Id = "wamid.IN", Timestamp = sentAt.ToUnixTimeSeconds().ToString(), Type = "text",
                    Text = new WhatsAppInboundText { Body = "Hello" },
                },
            },
        };

        await Processor().ProcessAsync(evt);

        var contact = _repo.Contacts["15551239999"];
        Assert.Equal("Diego", contact.Name);
        Assert.Equal("self", contact.Source);
        Assert.Equal("+15551239999", contact.PhoneE164);

        var msgs = await _repo.ListMessagesAsync("15551239999");
        Assert.Equal("in", msgs[0].Direction);
        Assert.Equal("Hello", msgs[0].Text);

        var conv = _repo.Conversations["15551239999"];
        Assert.Equal(1, conv.Unread);
        // Window = message time + 24h (not processing time).
        var expected = sentAt.AddHours(24);
        Assert.True((DateTimeOffset.Parse(conv.WindowExpiresAt!) - expected).Duration() < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MessageReceived_Media_CapturesMediaIdAndS3Key()
    {
        var evt = new AppInboundEvent
        {
            DetailType = "MessageReceived",
            Detail = new AppInboundDetail
            {
                MediaS3Key = "pn/15551239999/wamid.IMG/photo.jpg",
                Message = new WhatsAppInboundMessage
                {
                    From = "15551239999", Id = "wamid.IMG", Timestamp = "1700000000", Type = "image",
                    Image = new WhatsAppMediaInfo { Id = "media-1", MimeType = "image/jpeg" },
                },
            },
        };

        await Processor().ProcessAsync(evt);

        var m = (await _repo.ListMessagesAsync("15551239999"))[0];
        Assert.Equal("image", m.Type);
        Assert.Equal("media-1", m.MediaId);
        Assert.Equal("pn/15551239999/wamid.IMG/photo.jpg", m.MediaS3Key);
    }

    [Fact]
    public async Task StatusUpdated_CorrelatesByOpaqueRef_AndRecordsWamid()
    {
        await _repo.PutMessageAsync(new ChatMessage
        {
            WaId = "15551239999", Id = "local-1", Direction = "out", Type = "text", Text = "hi",
            Status = "sent", WaMessageId = "aws-send-id", SentBy = "agent1", CreatedAt = "t",
        });

        await Processor().ProcessAsync(new AppInboundEvent
        {
            DetailType = "StatusUpdated",
            Detail = new AppInboundDetail
            {
                // biz_opaque_callback_data echoes our message id; status.id is the Meta wamid.
                Status = new WhatsAppStatus { Id = "wamid.OUT", Status = "delivered", BizOpaqueCallbackData = "local-1" },
            },
        });

        var m = (await _repo.ListMessagesAsync("15551239999"))[0];
        Assert.Equal("delivered", m.Status);
        Assert.Equal("wamid.OUT", m.WaMessageId); // wamid recorded once learned
    }

    [Fact]
    public async Task StatusUpdated_Failed_CapturesErrorCodeAndDetail()
    {
        await _repo.PutMessageAsync(new ChatMessage
        {
            WaId = "15551239999", Id = "local-2", Direction = "out", Type = "text", Text = "hi",
            Status = "sent", SentBy = "agent1", CreatedAt = "t",
        });

        await Processor().ProcessAsync(new AppInboundEvent
        {
            DetailType = "StatusUpdated",
            Detail = new AppInboundDetail
            {
                Status = new WhatsAppStatus
                {
                    Id = "wamid.OUT", Status = "failed", BizOpaqueCallbackData = "local-2",
                    Errors =
                    [
                        new WhatsAppStatusError
                        {
                            Code = 131037,
                            ErrorData = new WhatsAppStatusErrorData { Details = "needs display name approval" },
                        },
                    ],
                },
            },
        });

        var m = (await _repo.ListMessagesAsync("15551239999"))[0];
        Assert.Equal("failed", m.Status);
        Assert.Equal(131037, m.ErrorCode);
        Assert.Equal("needs display name approval", m.ErrorDetail);
    }

    [Fact]
    public async Task StatusUpdated_WithoutOpaqueRef_IsIgnored()
    {
        await Processor().ProcessAsync(new AppInboundEvent
        {
            DetailType = "StatusUpdated",
            Detail = new AppInboundDetail { Status = new WhatsAppStatus { Id = "wamid.X", Status = "read" } },
        });
        // No throw, nothing to correlate.
    }

    [Fact]
    public async Task ButtonTap_HandleIt_WhileActive_PostsToHaMarksConsumedAndReactsChecked()
    {
        SeedBinding("tok1");
        _ha.Setup(h => h.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Processor().ProcessAsync(ButtonTap("H:tok1"));

        _ha.Verify(h => h.PostAsync("https://ha.example.com/api/webhook/abc", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("consumed", _repo.Bindings["tok1"].Status);
        _whatsapp.Verify(w => w.SendReactionAsync("+17742625384", "wamid.ALERT", "✅", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ButtonTap_HandleIt_AfterExpiry_DoesNotPostAndReactsHourglass()
    {
        SeedBinding("tok1", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

        await Processor().ProcessAsync(ButtonTap("H:tok1"));

        _ha.Verify(h => h.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("active", _repo.Bindings["tok1"].Status);
        _whatsapp.Verify(w => w.SendReactionAsync("+17742625384", "wamid.ALERT", "⌛", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ButtonTap_Ignore_DisablesBinding_SoLaterHandleDoesNotFire()
    {
        SeedBinding("tok1");

        await Processor().ProcessAsync(ButtonTap("I:tok1"));
        Assert.Equal("ignored", _repo.Bindings["tok1"].Status);
        _whatsapp.Verify(w => w.SendReactionAsync("+17742625384", "wamid.ALERT", "🚫", It.IsAny<CancellationToken>()), Times.Once);

        await Processor().ProcessAsync(ButtonTap("H:tok1"));
        _ha.Verify(h => h.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        _whatsapp.Verify(w => w.SendReactionAsync("+17742625384", "wamid.ALERT", "⌛", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ButtonTap_HandleIt_WhenHaPostFails_ReactsWarning()
    {
        SeedBinding("tok1");
        _ha.Setup(h => h.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Processor().ProcessAsync(ButtonTap("H:tok1"));

        Assert.Equal("consumed", _repo.Bindings["tok1"].Status);  // still fire-once
        _whatsapp.Verify(w => w.SendReactionAsync("+17742625384", "wamid.ALERT", "⚠️", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ButtonTap_UnknownToken_IsIgnored()
    {
        await Processor().ProcessAsync(ButtonTap("H:nope"));
        _ha.Verify(h => h.PostAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        _whatsapp.Verify(w => w.SendReactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
