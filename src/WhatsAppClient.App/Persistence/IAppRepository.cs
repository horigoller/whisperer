using WhatsAppClient.App.Models;

namespace WhatsAppClient.App.Persistence;

/// <summary>Single-table data access for the management app.</summary>
public interface IAppRepository
{
    Task<SystemUser?> GetUserAsync(string username, CancellationToken ct = default);
    Task PutUserAsync(SystemUser user, CancellationToken ct = default);
    Task<IReadOnlyList<SystemUser>> ListUsersAsync(CancellationToken ct = default);
    Task<int> CountUsersAsync(CancellationToken ct = default);
    Task DeleteUserAsync(string username, CancellationToken ct = default);

    Task<Contact?> GetContactAsync(string waId, CancellationToken ct = default);
    Task PutContactAsync(Contact contact, CancellationToken ct = default);
    Task<IReadOnlyList<Contact>> ListContactsAsync(CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(string waId, CancellationToken ct = default);
    Task PutConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default);

    /// <summary>Atomically zero a conversation's unread count without clobbering concurrent updates.</summary>
    Task ResetConversationUnreadAsync(string waId, CancellationToken ct = default);

    Task PutMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string waId, CancellationToken ct = default);
    Task<bool> PatchMessageStatusByWaMessageIdAsync(string waMessageId, string status, CancellationToken ct = default);

    Task PutAuthChallengeAsync(AuthChallenge challenge, CancellationToken ct = default);
    Task<AuthChallenge?> GetAuthChallengeAsync(string challengeId, CancellationToken ct = default);
    Task IncrementAuthAttemptsAsync(string challengeId, CancellationToken ct = default);
    Task DeleteAuthChallengeAsync(string challengeId, CancellationToken ct = default);
}
