using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Util;

namespace WhatsAppClient.App.Services;

public sealed class UserService
{
    private readonly IAppRepository _repo;

    public UserService(IAppRepository repo) => _repo = repo;

    public Task<IReadOnlyList<SystemUser>> ListAsync(CancellationToken ct = default) => _repo.ListUsersAsync(ct);

    public async Task<SystemUser> AddAsync(
        string username, string? displayName, string phoneInput, UserRole role, CancellationToken ct = default)
    {
        var user = new SystemUser
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName!,
            PhoneE164 = PhoneNumbers.ToE164(phoneInput),
            Role = role,
            Status = "active",
            CreatedAt = DateTime.UtcNow.ToString("o"),
        };
        await _repo.PutUserAsync(user, ct);
        return user;
    }

    public Task DeleteAsync(string username, CancellationToken ct = default) => _repo.DeleteUserAsync(username, ct);
}
