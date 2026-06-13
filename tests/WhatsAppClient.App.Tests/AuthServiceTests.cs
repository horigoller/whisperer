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
    private string? _sentBody;

    private AuthService CreateService(AppOptions? options = null)
    {
        _whatsapp
            .Setup(w => w.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, body, _, _) => _sentBody = body)
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.code"));

        options ??= new AppOptions
        {
            TableName = "T",
            BootstrapAdminUsername = "admin",
            BootstrapAdminPhone = "+15551112222",
            LoginTemplateName = "login_code",
        };
        return new AuthService(_repo, _whatsapp.Object, Options.Create(options), NullLogger<AuthService>.Instance);
    }

    private string ExtractCode() => Regex.Match(_sentBody!, @"\b(\d{6})\b").Groups[1].Value;

    [Fact]
    public async Task StartAsync_SeedsBootstrapAdmin_AndSendsCode()
    {
        var svc = CreateService();

        var challengeId = await svc.StartAsync("admin");

        Assert.NotEmpty(challengeId);
        Assert.Single(_repo.Users);
        Assert.True(_repo.Challenges.ContainsKey(challengeId));
        Assert.Matches(@"login code is \d{6}", _sentBody!);
        _whatsapp.Verify(w => w.SendTextMessageAsync("+15551112222", It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_UnknownUser_ReturnsChallengeButSendsNothing()
    {
        var svc = CreateService();
        _repo.Users["someone"] = new SystemUser { Username = "someone", DisplayName = "S", PhoneE164 = "+1", Role = UserRole.Agent };

        var challengeId = await svc.StartAsync("ghost");

        Assert.NotEmpty(challengeId);
        Assert.False(_repo.Challenges.ContainsKey(challengeId));
        _whatsapp.Verify(w => w.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
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
