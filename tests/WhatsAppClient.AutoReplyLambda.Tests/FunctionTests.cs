using Amazon.Lambda.TestUtilities;
using Moq;
using WhatsAppClient.AutoReplyLambda;
using WhatsAppClient.AutoReplyLambda.Models;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Models.Inbound;
using WhatsAppClient.Core.Services;
using Xunit;

namespace WhatsAppClient.AutoReplyLambda.Tests;

public class FunctionTests
{
    private const string ReplyText = "Thanks for your message!";

    private readonly Mock<IWhatsAppMessageService> _messageService = new();
    private readonly TestLambdaContext _context = new();

    private Function CreateFunction() => new(_messageService.Object, ReplyText);

    private static AutoReplyEvent EventFrom(string? from) => new()
    {
        Detail = new AutoReplyDetail
        {
            ContactName = "Diego",
            Message = new WhatsAppInboundMessage { From = from, Id = "wamid.IN", Type = "text" },
        },
    };

    [Fact]
    public async Task FunctionHandler_RepliesToSender_WithE164Recipient()
    {
        string? capturedTo = null;
        string? capturedBody = null;
        _messageService
            .Setup(s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((to, body, _, _) => { capturedTo = to; capturedBody = body; })
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.REPLY"));

        var result = await CreateFunction().FunctionHandler(EventFrom("17742625384"), _context);

        Assert.True(result.Replied);
        Assert.Equal("wamid.REPLY", result.MessageId);
        Assert.Equal("+17742625384", capturedTo); // wa_id normalized to E.164
        Assert.Equal(ReplyText, capturedBody);
    }

    [Fact]
    public async Task FunctionHandler_NoSender_SkipsWithoutThrowing()
    {
        var result = await CreateFunction().FunctionHandler(EventFrom(null), _context);

        Assert.False(result.Replied);
        _messageService.Verify(
            s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_PreservesLeadingPlus()
    {
        string? capturedTo = null;
        _messageService
            .Setup(s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((to, _, _, _) => capturedTo = to)
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.REPLY"));

        await CreateFunction().FunctionHandler(EventFrom("+17742625384"), _context);

        Assert.Equal("+17742625384", capturedTo);
    }
}
