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
    public async Task MessageReceived_SelfRegistersContact_StoresMessage_OpensWindow()
    {
        var evt = new AppInboundEvent
        {
            DetailType = "MessageReceived",
            Detail = new AppInboundDetail
            {
                ContactName = "Diego",
                Message = new WhatsAppInboundMessage
                {
                    From = "15551239999", Id = "wamid.IN", Timestamp = "1700000000", Type = "text",
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
        Assert.True(DateTimeOffset.Parse(conv.WindowExpiresAt!) > DateTimeOffset.UtcNow);
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
    public async Task StatusUpdated_PatchesSentMessage()
    {
        await _repo.PutMessageAsync(new ChatMessage
        {
            WaId = "15551239999", Id = "local-1", Direction = "out", Type = "text", Text = "hi",
            Status = "sent", WaMessageId = "wamid.OUT", SentBy = "agent1", CreatedAt = "t",
        });

        await Processor().ProcessAsync(new AppInboundEvent
        {
            DetailType = "StatusUpdated",
            Detail = new AppInboundDetail { Status = new WhatsAppStatus { Id = "wamid.OUT", Status = "delivered" } },
        });

        Assert.Equal("delivered", (await _repo.ListMessagesAsync("15551239999"))[0].Status);
    }
}
