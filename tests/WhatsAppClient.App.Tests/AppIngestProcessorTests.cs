using Microsoft.Extensions.Logging.Abstractions;
using WhatsAppClient.App.Ingest;
using WhatsAppClient.App.Models;
using WhatsAppClient.Core.Models.Inbound;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class AppIngestProcessorTests
{
    private readonly InMemoryAppRepository _repo = new();
    private AppIngestProcessor Processor() => new(_repo, NullLogger<AppIngestProcessor>.Instance);

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
    public async Task StatusUpdated_WithoutOpaqueRef_IsIgnored()
    {
        await Processor().ProcessAsync(new AppInboundEvent
        {
            DetailType = "StatusUpdated",
            Detail = new AppInboundDetail { Status = new WhatsAppStatus { Id = "wamid.X", Status = "read" } },
        });
        // No throw, nothing to correlate.
    }
}
