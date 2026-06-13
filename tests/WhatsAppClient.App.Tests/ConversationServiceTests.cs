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

    public ConversationServiceTests()
    {
        _repo.Contacts[WaId] = new Contact { WaId = WaId, PhoneE164 = $"+{WaId}", Name = "Cust", Source = "self" };
        _whatsapp.Setup(w => w.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.out"));
        _whatsapp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WhatsAppTemplateComponent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.tmpl"));
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
        _whatsapp.Verify(w => w.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_WindowOpen_SendsAndPersists()
    {
        _repo.Conversations[WaId] = new Conversation
        {
            WaId = WaId, LastActivityAt = "t", Unread = 2,
            WindowExpiresAt = DateTime.UtcNow.AddMinutes(30).ToString("o"),
        };

        var msg = await Svc().ReplyAsync(WaId, "hello back", "agent1");

        Assert.Equal("out", msg.Direction);
        Assert.Equal("agent1", msg.SentBy);
        _whatsapp.Verify(w => w.SendTextMessageAsync($"+{WaId}", "hello back", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        var stored = await _repo.ListMessagesAsync(WaId);
        Assert.Single(stored);
    }

    [Fact]
    public async Task SendTemplateAsync_WorksWithoutOpenWindow()
    {
        var msg = await Svc().SendTemplateAsync(WaId, "first_contact_greeting", "en_US", ["Hori"], "agent1");

        Assert.Equal("template", msg.Type);
        Assert.Equal("first_contact_greeting", msg.TemplateName);
        _whatsapp.Verify(w => w.SendTemplateMessageAsync($"+{WaId}", "first_contact_greeting", "en_US", It.IsAny<IReadOnlyList<WhatsAppTemplateComponent>>(), It.IsAny<CancellationToken>()), Times.Once);
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
