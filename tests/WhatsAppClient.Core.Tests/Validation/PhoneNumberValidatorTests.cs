using WhatsAppClient.Core.Validation;
using Xunit;

namespace WhatsAppClient.Core.Tests.Validation;

public class PhoneNumberValidatorTests
{
    [Theory]
    [InlineData("+15551234567")]
    [InlineData("+447911123456")]
    [InlineData("+12345678")]
    public void IsValidE164_ReturnsTrue_ForWellFormedNumbers(string phoneNumber)
    {
        Assert.True(PhoneNumberValidator.IsValidE164(phoneNumber));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("15551234567")] // missing leading '+'
    [InlineData("+0123456789")] // leading zero after '+'
    [InlineData("+1234")] // too short
    [InlineData("+1234567890123456")] // too long
    [InlineData("+1555 123 4567")] // contains spaces
    [InlineData("+1-555-123-4567")] // contains hyphens
    public void IsValidE164_ReturnsFalse_ForMalformedNumbers(string? phoneNumber)
    {
        Assert.False(PhoneNumberValidator.IsValidE164(phoneNumber));
    }
}
