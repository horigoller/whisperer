using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Persistence;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.App.Services;

/// <summary>Whether a login code failed to deliver, and why.</summary>
public sealed record CodeDelivery(bool Failed, int? ErrorCode, string? ErrorDetail);

public sealed class AuthService
{
    private readonly IAppRepository _repo;
    private readonly IWhatsAppMessageService _whatsapp;
    private readonly ITemplateService _templates;
    private readonly AppOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAppRepository repo,
        IWhatsAppMessageService whatsapp,
        ITemplateService templates,
        IOptions<AppOptions> options,
        ILogger<AuthService> logger)
    {
        _repo = repo;
        _whatsapp = whatsapp;
        _templates = templates;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Start login: send a one-time code to the user's WhatsApp. Always returns a challenge id.</summary>
    public async Task<string> StartAsync(string username, CancellationToken ct = default)
    {
        var challengeId = Guid.NewGuid().ToString();
        var user = await ResolveUserAsync(username, ct);

        // No user enumeration: always return a challengeId; only send a code if the user exists
        // and isn't in a cooldown (so the public endpoint can't be used to spam login codes).
        if (user is { Status: "active" } && await _repo.TryStartLoginAsync(user.Username, LoginCodes.CooldownSeconds, ct))
        {
            var code = LoginCodes.Generate();
            await _repo.PutAuthChallengeAsync(new AuthChallenge
            {
                ChallengeId = challengeId,
                Username = user.Username,
                CodeHash = LoginCodes.Hash(challengeId, code),
                Attempts = 0,
                Ttl = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + LoginCodes.TtlSeconds,
            }, ct);

            try
            {
                await DeliverCodeAsync(challengeId, user.PhoneE164, code, ct);
            }
            catch (Exception ex)
            {
                // Synchronous send failure — record it so the login page can explain.
                _logger.LogError(ex, "Failed to deliver login code");
                await _repo.PatchAuthDeliveryErrorAsync(challengeId, null, ex.Message, ct);
            }
        }

        return challengeId;
    }

    /// <summary>Delivery status of a login code, so the login page can explain a non-arriving code.</summary>
    public async Task<CodeDelivery> GetCodeDeliveryAsync(string challengeId, CancellationToken ct = default)
    {
        var challenge = await _repo.GetAuthChallengeAsync(challengeId, ct);
        return challenge?.DeliveryError is { } detail
            ? new CodeDelivery(true, challenge.DeliveryErrorCode, detail)
            : new CodeDelivery(false, null, null);
    }

    /// <summary>Verify a code; returns the authenticated user or null.</summary>
    public async Task<SessionUser?> VerifyAsync(string challengeId, string code, CancellationToken ct = default)
    {
        var challenge = await _repo.GetAuthChallengeAsync(challengeId, ct);
        if (challenge is null) return null;

        if (challenge.Ttl < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            challenge.Attempts >= LoginCodes.MaxAttempts)
        {
            await _repo.DeleteAuthChallengeAsync(challengeId, ct);
            return null;
        }

        if (!LoginCodes.Matches(challengeId, code, challenge.CodeHash))
        {
            await _repo.IncrementAuthAttemptsAsync(challengeId, ct);
            return null;
        }

        var user = await _repo.GetUserAsync(challenge.Username, ct);
        await _repo.DeleteAuthChallengeAsync(challengeId, ct);
        return user is null ? null : new SessionUser(user.Username, user.Role, user.DisplayName);
    }

    /// <summary>Look up the user; seed the bootstrap admin on first use so the first login works.</summary>
    private async Task<SystemUser?> ResolveUserAsync(string username, CancellationToken ct)
    {
        var existing = await _repo.GetUserAsync(username, ct);
        if (existing is not null) return existing;

        if (!string.IsNullOrEmpty(_options.BootstrapAdminUsername) &&
            !string.IsNullOrEmpty(_options.BootstrapAdminPhone) &&
            username.Equals(_options.BootstrapAdminUsername, StringComparison.OrdinalIgnoreCase) &&
            await _repo.CountUsersAsync(ct) == 0)
        {
            var admin = new SystemUser
            {
                Username = _options.BootstrapAdminUsername,
                DisplayName = _options.BootstrapAdminUsername,
                PhoneE164 = _options.BootstrapAdminPhone,
                Role = UserRole.Admin,
                Status = "active",
                CreatedAt = DateTime.UtcNow.ToString("o"),
            };
            await _repo.PutUserAsync(admin, ct);
            return admin;
        }
        return null;
    }

    private async Task DeliverCodeAsync(string challengeId, string phoneE164, string code, CancellationToken ct)
    {
        // Prefer the AUTHENTICATION template — it delivers even outside the recipient's 24h window
        // (free-form would fail there with 131047). Only use it when actually approved: an
        // unapproved/nonexistent template "succeeds" then fails asynchronously, which would silently
        // break in-window logins. Falls back to free-form (in-window) until the template clears.
        // Both carry biz_opaque_callback_data = challengeId for async delivery-failure correlation.
        var useTemplate = !string.IsNullOrEmpty(_options.LoginTemplateName)
            && await IsLoginTemplateApprovedAsync(ct);
        if (useTemplate)
        {
            await _whatsapp.SendMessageAsync(BuildLoginTemplate(challengeId, phoneE164, code), ct);
            return;
        }

        await _whatsapp.SendMessageAsync(new WhatsAppTextMessage
        {
            To = phoneE164,
            Text = new WhatsAppTextBody { Body = $"Your Whisperer login code is {code}. It expires in 5 minutes." },
            BizOpaqueCallbackData = challengeId,
        }, ct);
    }

    private async Task<bool> IsLoginTemplateApprovedAsync(CancellationToken ct)
    {
        try { return await _templates.IsApprovedAsync(_options.LoginTemplateName, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not check login template approval"); return false; }
    }

    private WhatsAppTemplateMessage BuildLoginTemplate(string challengeId, string phoneE164, string code) => new()
    {
        To = phoneE164,
        BizOpaqueCallbackData = challengeId,
        Template = new WhatsAppTemplate
        {
            Name = _options.LoginTemplateName,
            Language = new WhatsAppTemplateLanguage { Code = "en_US" },
            // Authentication template: the code fills the body AND the copy-code (OTP) button.
            Components = new[]
            {
                new WhatsAppTemplateComponent { Type = "body", Parameters = new[] { WhatsAppTemplateParameter.FromText(code) } },
                new WhatsAppTemplateComponent { Type = "button", SubType = "url", Index = "0", Parameters = new[] { WhatsAppTemplateParameter.FromText(code) } },
            },
        },
    };
}
