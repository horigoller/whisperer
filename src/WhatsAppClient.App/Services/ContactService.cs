using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.App.Util;

namespace WhatsAppClient.App.Services;

public sealed class ContactService
{
    private readonly IAppRepository _repo;

    public ContactService(IAppRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Contact>> ListAsync(CancellationToken ct = default) => _repo.ListContactsAsync(ct);

    /// <summary>Agent-added contact. Self-registration on inbound is handled by the ingest Lambda.</summary>
    public async Task<Contact> AddAsync(string? name, string phoneInput, CancellationToken ct = default)
    {
        var phoneE164 = PhoneNumbers.ToE164(phoneInput);
        var waId = PhoneNumbers.ToWaId(phoneE164);

        var existing = await _repo.GetContactAsync(waId, ct);
        if (existing is not null) return existing;

        var now = DateTime.UtcNow.ToString("o");
        var contact = new Contact { WaId = waId, PhoneE164 = phoneE164, Name = name, Source = "manual", CreatedAt = now };
        await _repo.PutContactAsync(contact, ct);

        // Seed an (empty, window-closed) conversation so it shows up in the inbox.
        await _repo.PutConversationAsync(new Conversation
        {
            WaId = waId,
            Name = name,
            LastPreview = null,
            LastActivityAt = now,
            WindowExpiresAt = null,
            Unread = 0,
        }, ct);
        return contact;
    }
}
