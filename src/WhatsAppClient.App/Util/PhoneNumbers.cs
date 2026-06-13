namespace WhatsAppClient.App.Util;

public static class PhoneNumbers
{
    /// <summary>Normalizes free-form input to E.164 ("+&lt;digits&gt;").</summary>
    public static string ToE164(string input)
    {
        var digits = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) throw new ArgumentException("Invalid phone number.", nameof(input));
        return $"+{digits}";
    }

    /// <summary>The WhatsApp id form (digits only, no '+').</summary>
    public static string ToWaId(string input) =>
        new((input ?? string.Empty).Where(char.IsDigit).ToArray());
}
