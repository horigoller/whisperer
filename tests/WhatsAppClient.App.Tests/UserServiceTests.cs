using WhatsAppClient.App.Models;
using WhatsAppClient.App.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class UserServiceTests
{
    private readonly InMemoryAppRepository _repo = new();
    private UserService Svc() => new(_repo);

    [Fact]
    public async Task AddAsync_NormalizesPhone_AndDefaultsDisplayName()
    {
        var user = await Svc().AddAsync("agent1", null, "+1 (774) 262-5384", UserRole.Agent);

        Assert.Equal("agent1", user.DisplayName); // falls back to username
        Assert.Equal("+17742625384", user.PhoneE164);
        Assert.Equal("active", user.Status);
        Assert.True(_repo.Users.ContainsKey("agent1"));
    }

    [Fact]
    public async Task UpdateAsync_ChangesProfile_KeepsCreatedAt()
    {
        var svc = Svc();
        var created = await svc.AddAsync("agent1", "Agent One", "+15551112222", UserRole.Agent);

        var updated = await svc.UpdateAsync("agent1", "Renamed", "+17742625384", UserRole.Admin);

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.DisplayName);
        Assert.Equal("+17742625384", updated.PhoneE164);
        Assert.Equal(UserRole.Admin, updated.Role);
        Assert.Equal(created.CreatedAt, updated.CreatedAt); // identity/metadata preserved
    }

    [Fact]
    public async Task UpdateAsync_UnknownUser_ReturnsNull()
    {
        Assert.Null(await Svc().UpdateAsync("ghost", "X", "+15551112222", UserRole.Agent));
    }

    [Fact]
    public async Task DeleteAsync_RemovesUser()
    {
        var svc = Svc();
        await svc.AddAsync("agent1", "Agent One", "+15551112222", UserRole.Agent);

        await svc.DeleteAsync("agent1");

        Assert.False(_repo.Users.ContainsKey("agent1"));
    }
}
