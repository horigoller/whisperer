namespace WhatsAppClient.Api;

public sealed record StartLoginRequest(string? Username);
public sealed record VerifyRequest(string? ChallengeId, string? Code);
public sealed record ReplyRequest(string? Text);
public sealed record TemplateSendRequest(string? TemplateName, string? LanguageCode, List<string>? Params);
public sealed record AddContactRequest(string? Name, string? PhoneE164);
public sealed record AddUserRequest(string? Username, string? DisplayName, string? PhoneE164, string? Role);
public sealed record UpdateUserRequest(string? DisplayName, string? PhoneE164, string? Role);
