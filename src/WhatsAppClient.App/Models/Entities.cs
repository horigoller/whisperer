namespace WhatsAppClient.App.Models;

public enum UserRole { Agent, Admin }

public sealed class SystemUser
{
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string PhoneE164 { get; set; }
    public UserRole Role { get; set; } = UserRole.Agent;
    public string Status { get; set; } = "active";
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class Contact
{
    /// <summary>The customer's WhatsApp id (phone digits, no '+').</summary>
    public required string WaId { get; set; }
    public required string PhoneE164 { get; set; }
    public string? Name { get; set; }

    /// <summary>"self" (self-registered on inbound) or "manual" (added by an agent).</summary>
    public string Source { get; set; } = "manual";
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class Conversation
{
    public required string WaId { get; set; }
    public string? Name { get; set; }
    public string? LastPreview { get; set; }
    public string LastActivityAt { get; set; } = string.Empty;

    /// <summary>ISO timestamp; when in the future, free-form replies are allowed.</summary>
    public string? WindowExpiresAt { get; set; }
    public int Unread { get; set; }
}

public sealed class ChatMessage
{
    public required string WaId { get; set; }
    public required string Id { get; set; }

    /// <summary>"in" or "out".</summary>
    public required string Direction { get; set; }
    public string Type { get; set; } = "text";
    public string? Text { get; set; }
    public string? MediaId { get; set; }
    public string? MediaS3Key { get; set; }
    public string Status { get; set; } = "sent";
    public string? WaMessageId { get; set; }
    public string? SentBy { get; set; }
    public string? TemplateName { get; set; }

    /// <summary>WhatsApp error code when <see cref="Status"/> is "failed" (e.g. 131037).</summary>
    public int? ErrorCode { get; set; }

    /// <summary>Human-readable failure reason when <see cref="Status"/> is "failed".</summary>
    public string? ErrorDetail { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class AuthChallenge
{
    public required string ChallengeId { get; set; }
    public required string Username { get; set; }
    public required string CodeHash { get; set; }
    public int Attempts { get; set; }

    /// <summary>Unix epoch seconds; DynamoDB TTL attribute.</summary>
    public long Ttl { get; set; }
}

/// <summary>Authenticated principal carried in the session JWT.</summary>
public sealed record SessionUser(string Username, UserRole Role, string DisplayName);
