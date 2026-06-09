using System.Text.RegularExpressions;

namespace WhatsAppClient.Core.Validation;

/// <summary>
/// Validates recipient phone numbers against the E.164 format required by the
/// WhatsApp Business Platform.
/// </summary>
public static partial class PhoneNumberValidator
{
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164Regex();

    /// <summary>
    /// Returns true if <paramref name="phoneNumber"/> is a valid E.164 phone number
    /// (a leading '+' followed by 7 to 15 digits, the first of which is non-zero).
    /// </summary>
    public static bool IsValidE164(string? phoneNumber) =>
        !string.IsNullOrWhiteSpace(phoneNumber) && E164Regex().IsMatch(phoneNumber);
}
