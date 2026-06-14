using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Services;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class AuthServiceTests
{
    private readonly InMemoryAppRepository _repo = new();
    private readonly Mock<IWhatsAppMessageService> _whatsapp = new();
    private readonly Mock<ITemplateService> _templates = new();
    private WhatsAppMessage? _sent;
    private string? _sentBody; // the 6-digit code, extracted from whichever message type was sent

    private AuthService CreateService(AppOptions? options = null, bool templateApproved = false)
    {
        _templates.Setup(t => t.IsApprovedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(templateApproved);

        _whatsapp
            .Setup(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppMessage, CancellationToken>((m, _) =>
            {
                _sent = m;
                var raw = m switch
                {
                    WhatsAppTextMessage t => t.Text.Body,
                    WhatsAppTemplateMessage tm => tm.Template.Components?.FirstOrDefault(c => c.Type == "body")?.Parameters.FirstOrDefault()?.Text,
                    _ => null,
                };
                _sentBody = raw is null ? null : Regex.Match(raw, @"\d{6}").Value;
            })
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.code"));

        options ??= new AppOptions
        {
            TableName = "T",
            BootstrapAdminUsername = "admin",
            BootstrapAdminPhone = "+15551112222",
            LoginTemplateName = "login_code",
        };
        return new AuthService(_repo, _whatsapp.Object, _templates.Object, Options.Create(options), NullLogger<AuthService>.Instance);
    }

    private string ExtractCode() => _sentBody!;

    [Fact]
    public async Task StartAsync_SeedsBootstrapAdmin_AndSendsCode()
    {
        var svc = CreateService();

        var challengeId = await svc.StartAsync("admin");

        Assert.NotEmpty(challengeId);
        Assert.Single(_repo.Users);
        Assert.True(_repo.Challenges.ContainsKey(challengeId));
        Assert.Matches(@"^\d{6}$", _sentBody!); // 6-digit code (template body parameter)
        _whatsapp.Verify(w => w.SendMessageAsync(It.Is<WhatsAppMessage>(m => m.To == "+15551112222"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_TemplateNotApproved_SendsFreeFormText()
    {
        await CreateService(templateApproved: false).StartAsync("admin");
        Assert.IsType<WhatsAppTextMessage>(_sent);
        Assert.Matches(@"^\d{6}$", _sentBody!);
    }

    [Fact]
    public async Task StartAsync_TemplateApproved_SendsAuthenticationTemplate()
    {
        await CreateService(templateApproved: true).StartAsync("admin");
        var tmpl = Assert.IsType<WhatsAppTemplateMessage>(_sent);
        Assert.Equal("login_code", tmpl.Template.Name);
        // body + copy-code button both carry the code
        Assert.Equal(2, tmpl.Template.Components!.Count);
        Assert.Matches(@"^\d{6}$", _sentBody!);
    }

    [Fact]
    public async Task StartAsync_UnknownUser_ReturnsChallengeButSendsNothing()
    {
        var svc = CreateService();
        _repo.Users["someone"] = new SystemUser { Username = "someone", DisplayName = "S", PhoneE164 = "+1", Role = UserRole.Agent };

        var challengeId = await svc.StartAsync("ghost");

        Assert.NotEmpty(challengeId);
        Assert.False(_repo.Challenges.ContainsKey(challengeId));
        _whatsapp.Verify(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_SecondCallWithinCooldown_SendsNoSecondCode()
    {
        var svc = CreateService();

        await svc.StartAsync("admin");
        _sentBody = null; // reset capture
        await svc.StartAsync("admin"); // immediate retry → should be rate-limited

        Assert.Null(_sentBody);
        _whatsapp.Verify(w => w.SendMessageAsync(It.IsAny<WhatsAppMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_CorrectCode_ReturnsUser_AndConsumesChallenge()
    {
        var svc = CreateService();
        var challengeId = await svc.StartAsync("admin");

        var bad = await svc.VerifyAsync(challengeId, "000000");
        Assert.Null(bad);
        Assert.Equal(1, _repo.Challenges[challengeId].Attempts);

        var user = await svc.VerifyAsync(challengeId, ExtractCode());
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(_repo.Challenges.ContainsKey(challengeId));
    }

    [Fact]
    public async Task VerifyAsync_ExpiredCode_ReturnsNull()
    {
        var svc = CreateService();
        var challengeId = await svc.StartAsync("admin");
        _repo.Challenges[challengeId].Ttl = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;

        Assert.Null(await svc.VerifyAsync(challengeId, ExtractCode()));
        Assert.False(_repo.Challenges.ContainsKey(challengeId));
    }
}
