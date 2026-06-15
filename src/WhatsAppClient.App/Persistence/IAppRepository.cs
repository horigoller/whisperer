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
    Task<ChatMessage?> GetMessageAsync(string waId, string id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string waId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListMessagesAfterAsync(string waId, string afterCreatedAt, CancellationToken ct = default);

    /// <summary>
    /// Patch an outbound message's status, located by the opaque id we sent as
    /// <c>biz_opaque_callback_data</c>; records the Meta wamid when provided. Returns false if no match.
    /// </summary>
    Task<bool> PatchMessageStatusByRefAsync(
        string opaqueId, string status, string? metaMessageId,
        int? errorCode = null, string? errorDetail = null, CancellationToken ct = default);

    /// <summary>
    /// Returns true if a login code may be sent to <paramref name="username"/> now, atomically
    /// recording a cooldown so concurrent/rapid requests can't spam codes. False = still cooling down.
    /// </summary>
    Task<bool> TryStartLoginAsync(string username, int cooldownSeconds, CancellationToken ct = default);

    Task PutAuthChallengeAsync(AuthChallenge challenge, CancellationToken ct = default);
    Task<AuthChallenge?> GetAuthChallengeAsync(string challengeId, CancellationToken ct = default);

    /// <summary>Record a login-code delivery failure on the challenge, if it still exists (no-op otherwise).</summary>
    Task PatchAuthDeliveryErrorAsync(string challengeId, int? errorCode, string? errorDetail, CancellationToken ct = default);

    // ---- WebSocket connections ---------------------------------------------
    Task PutConnectionAsync(string connectionId, string username, CancellationToken ct = default);
    Task DeleteConnectionAsync(string connectionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListConnectionIdsAsync(CancellationToken ct = default);
    Task IncrementAuthAttemptsAsync(string challengeId, CancellationToken ct = default);
    Task DeleteAuthChallengeAsync(string challengeId, CancellationToken ct = default);
}
