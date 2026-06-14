using Moq;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Services;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class ConversationServiceTests
{
    private const string WaId = "15551230000";
    private readonly InMemoryAppRepository _repo = new();
    private readonly Mock<IWhatsAppMessageService> _whatsapp = new();
    private WhatsAppMessage? _sent;

    public ConversationServiceTests()
    {
        _repo.Contacts[WaId] = new Contact { WaId = WaId, PhoneE164 = $"+{WaId}", Name = "Cust", Source = "self" };
        _whatsapp.Setup(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppMessage, CancellationToken>((m, _) => _sent = m)
            .ReturnsAsync(new SendWhatsAppMessageResult("aws-send-id"));
    }

    private ConversationService Svc() => new(_repo, _whatsapp.Object);

    [Fact]
    public async Task ReplyAsync_WindowClosed_Throws()
    {
        _repo.Conversations[WaId] = new Conversation
        {
            WaId = WaId, LastActivityAt = "t",
            WindowExpiresAt = DateTime.UtcNow.AddMinutes(-1).ToString("o"),
        };

        await Assert.ThrowsAsync<WindowClosedException>(() => Svc().ReplyAsync(WaId, "hi", "agent1"));
        _whatsapp.Verify(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_WindowOpen_SendsWithOpaqueIdAndPersists()
    {
        _repo.Conversations[WaId] = new Conversation
        {
            WaId = WaId, LastActivityAt = "t", Unread = 2,
            WindowExpiresAt = DateTime.UtcNow.AddMinutes(30).ToString("o"),
        };

        var msg = await Svc().ReplyAsync(WaId, "hello back", "agent1");

        var text = Assert.IsType<WhatsAppTextMessage>(_sent);
        Assert.Equal($"+{WaId}", text.To);
        Assert.Equal("hello back", text.Text.Body);
        Assert.Equal(msg.Id, text.BizOpaqueCallbackData); // opaque correlation == stored id
        Assert.Equal("out", msg.Direction);
        Assert.Single(await _repo.ListMessagesAsync(WaId));
    }

    [Fact]
    public async Task SendTemplateAsync_WorksWithoutOpenWindow_AndSetsOpaqueId()
    {
        var msg = await Svc().SendTemplateAsync(WaId, "first_contact_greeting", "en_US", ["Hori"], "agent1");

        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        Assert.Equal("first_contact_greeting", tmpl.Template.Name);
        Assert.Equal(msg.Id, tmpl.BizOpaqueCallbackData);
        Assert.Equal("template", msg.Type);
    }

    [Fact]
    public async Task ReplyAsync_UnknownContact_Throws()
    {
        await Assert.ThrowsAsync<ContactNotFoundException>(() => Svc().ReplyAsync("99999999999", "hi", "agent1"));
    }

    [Fact]
    public async Task GetThreadAsync_ClearsUnread()
    {
        _repo.Conversations[WaId] = new Conversation { WaId = WaId, LastActivityAt = "t", Unread = 3 };
        await _repo.PutMessageAsync(new ChatMessage { WaId = WaId, Id = "m1", Direction = "in", Type = "text", Text = "hi", CreatedAt = "t" });

        var (messages, conv) = await Svc().GetThreadAsync(WaId);

        Assert.Single(messages);
        Assert.Equal(0, conv!.Unread);
        Assert.Equal(0, _repo.Conversations[WaId].Unread);
    }
}
