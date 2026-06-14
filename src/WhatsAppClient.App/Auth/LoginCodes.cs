using System.Security.Cryptography;
using System.Text;

namespace WhatsAppClient.App.Auth;

/// <summary>One-time login code generation and verification.</summary>
public static class LoginCodes
{
    public const int TtlSeconds = 300; // 5 minutes
    public const int MaxAttempts = 5;
    public const int CooldownSeconds = 45; // min interval between login codes per user

    /// <summary>A 6-digit numeric code.</summary>
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>Hash the code bound to its challenge id so a leaked hash can't be reused elsewhere.</summary>
    public static string Hash(string challengeId, string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{code}"));
        return Convert.ToHexString(bytes);
    }

    public static bool Matches(string challengeId, string code, string expectedHash)
    {
        var actual = Encoding.UTF8.GetBytes(Hash(challengeId, code));
        var expected = Encoding.UTF8.GetBytes(expectedHash ?? string.Empty);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
