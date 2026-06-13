using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;

namespace WhatsAppClient.App.Tests;

/// <summary>In-memory <see cref="IAppRepository"/> for service/processor unit tests.</summary>
public sealed class InMemoryAppRepository : IAppRepository
{
    public Dictionary<string, SystemUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Contact> Contacts { get; } = new();
    public Dictionary<string, Conversation> Conversations { get; } = new();
    public Dictionary<string, List<ChatMessage>> Messages { get; } = new();
    public Dictionary<string, AuthChallenge> Challenges { get; } = new();

    public Task<SystemUser?> GetUserAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(Users.GetValueOrDefault(username));
    public Task PutUserAsync(SystemUser user, CancellationToken ct = default) { Users[user.Username] = user; return Task.CompletedTask; }
    public Task<IReadOnlyList<SystemUser>> ListUsersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SystemUser>>(Users.Values.ToList());
    public Task<int> CountUsersAsync(CancellationToken ct = default) => Task.FromResult(Users.Count);
    public Task DeleteUserAsync(string username, CancellationToken ct = default) { Users.Remove(username); return Task.CompletedTask; }

    public Task<Contact?> GetContactAsync(string waId, CancellationToken ct = default) =>
        Task.FromResult(Contacts.GetValueOrDefault(waId));
    public Task PutContactAsync(Contact contact, CancellationToken ct = default) { Contacts[contact.WaId] = contact; return Task.CompletedTask; }
    public Task<IReadOnlyList<Contact>> ListContactsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Contact>>(Contacts.Values.ToList());

    public Task<Conversation?> GetConversationAsync(string waId, CancellationToken ct = default) =>
        Task.FromResult(Conversations.GetValueOrDefault(waId));
    public Task PutConversationAsync(Conversation conversation, CancellationToken ct = default) { Conversations[conversation.WaId] = conversation; return Task.CompletedTask; }
    public Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Conversation>>(
            Conversations.Values.OrderByDescending(c => c.LastActivityAt, StringComparer.Ordinal).ToList());
    public Task ResetConversationUnreadAsync(string waId, CancellationToken ct = default)
    {
        if (Conversations.TryGetValue(waId, out var c)) c.Unread = 0;
        return Task.CompletedTask;
    }

    public Task PutMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        if (!Messages.TryGetValue(message.WaId, out var list)) Messages[message.WaId] = list = [];
        list.Add(message);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string waId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(
            (Messages.GetValueOrDefault(waId) ?? []).OrderBy(m => m.CreatedAt, StringComparer.Ordinal).ToList());

    public Task<bool> PatchMessageStatusByWaMessageIdAsync(string waMessageId, string status, CancellationToken ct = default)
    {
        foreach (var list in Messages.Values)
        {
            var m = list.FirstOrDefault(x => x.WaMessageId == waMessageId);
            if (m is not null) { m.Status = status; return Task.FromResult(true); }
        }
        return Task.FromResult(false);
    }

    public Task PutAuthChallengeAsync(AuthChallenge challenge, CancellationToken ct = default) { Challenges[challenge.ChallengeId] = challenge; return Task.CompletedTask; }
    public Task<AuthChallenge?> GetAuthChallengeAsync(string challengeId, CancellationToken ct = default) =>
        Task.FromResult(Challenges.GetValueOrDefault(challengeId));
    public Task IncrementAuthAttemptsAsync(string challengeId, CancellationToken ct = default)
    {
        if (Challenges.TryGetValue(challengeId, out var c)) c.Attempts++;
        return Task.CompletedTask;
    }
    public Task DeleteAuthChallengeAsync(string challengeId, CancellationToken ct = default) { Challenges.Remove(challengeId); return Task.CompletedTask; }
}
