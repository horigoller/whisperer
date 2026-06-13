using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WhatsAppClient.App.Models;

namespace WhatsAppClient.App.Auth;

public interface ISessionTokenService
{
    Task<string> IssueAsync(SessionUser user, CancellationToken ct = default);
    Task<SessionUser?> ValidateAsync(string token, CancellationToken ct = default);
}

/// <summary>HS256 JWT sessions signed with the secret from <see cref="ISessionSecretProvider"/>.</summary>
public sealed class SessionTokenService : ISessionTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly ISessionSecretProvider _secretProvider;

    public SessionTokenService(ISessionSecretProvider secretProvider) => _secretProvider = secretProvider;

    public async Task<string> IssueAsync(SessionUser user, CancellationToken ct = default)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(await _secretProvider.GetSecretAsync(ct)));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim("role", user.Role.ToString()),
                new Claim("name", user.DisplayName),
            ],
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<SessionUser?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(await _secretProvider.GetSecretAsync(ct)));
        try
        {
            // Keep raw JWT claim names ("sub"/"role"/"name") instead of the legacy SOAP mappings.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            var username = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(username)) return null;
            var role = Enum.TryParse<UserRole>(principal.FindFirst("role")?.Value, out var r) ? r : UserRole.Agent;
            return new SessionUser(username, role, principal.FindFirst("name")?.Value ?? username);
        }
        catch
        {
            return null;
        }
    }
}
