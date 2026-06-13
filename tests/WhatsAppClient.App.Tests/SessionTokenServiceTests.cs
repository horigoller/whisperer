using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Models;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class SessionTokenServiceTests
{
    private sealed class FixedSecret : ISessionSecretProvider
    {
        public Task<string> GetSecretAsync(CancellationToken ct = default) =>
            Task.FromResult("a-very-long-test-signing-secret-of-many-bytes");
    }

    [Fact]
    public async Task IssueThenValidate_RoundTripsClaims()
    {
        var svc = new SessionTokenService(new FixedSecret());
        var token = await svc.IssueAsync(new SessionUser("hori", UserRole.Admin, "Hori Goller"));

        var user = await svc.ValidateAsync(token);

        Assert.NotNull(user);
        Assert.Equal("hori", user!.Username);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal("Hori Goller", user.DisplayName);
    }

    [Fact]
    public async Task Validate_TamperedToken_ReturnsNull()
    {
        var svc = new SessionTokenService(new FixedSecret());
        var token = await svc.IssueAsync(new SessionUser("hori", UserRole.Agent, "Hori"));

        Assert.Null(await svc.ValidateAsync(token + "tampered"));
        Assert.Null(await svc.ValidateAsync("not-a-jwt"));
    }
}
