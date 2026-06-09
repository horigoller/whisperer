using Amazon.Lambda.TestUtilities;
using Moq;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using WhatsAppClient.Lambda.Models;
using Xunit;

namespace WhatsAppClient.Lambda.Tests;

public class FunctionTests
{
    private readonly Mock<IWhatsAppMessageService> _messageService = new();
    private readonly TestLambdaContext _context = new();

    private Function CreateFunction() => new(_messageService.Object);

    [Fact]
    public async Task FunctionHandler_WithTextMessage_CallsSendTextMessageAndReturnsMessageId()
    {
        _messageService
            .Setup(s => s.SendTextMessageAsync("+15551234567", "Hello!", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.abc123"));

        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput { To = "+15551234567", MessageType = "text", Text = "Hello!", PreviewUrl = true },
            _context);

        Assert.True(result.Success);
        Assert.Equal("wamid.abc123", result.MessageId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task FunctionHandler_WithTextMessage_IsCaseInsensitiveForMessageType()
    {
        _messageService
            .Setup(s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.abc123"));

        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput { To = "+15551234567", MessageType = "TEXT", Text = "Hello!" },
            _context);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task FunctionHandler_WithTextMessage_MissingText_ReturnsFailure()
    {
        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput { To = "+15551234567", MessageType = "text" },
            _context);

        Assert.False(result.Success);
        Assert.Null(result.MessageId);
        Assert.NotNull(result.ErrorMessage);

        _messageService.Verify(
            s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_WithTemplateMessage_MapsComponentsAndReturnsMessageId()
    {
        IReadOnlyList<WhatsAppTemplateComponent>? capturedComponents = null;
        _messageService
            .Setup(s => s.SendTemplateMessageAsync(
                "+15551234567", "order_confirmation", "en_US", It.IsAny<IReadOnlyList<WhatsAppTemplateComponent>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IReadOnlyList<WhatsAppTemplateComponent>?, CancellationToken>(
                (_, _, _, components, _) => capturedComponents = components)
            .ReturnsAsync(new SendWhatsAppMessageResult("wamid.def456"));

        var function = CreateFunction();

        var input = new SendMessageInput
        {
            To = "+15551234567",
            MessageType = "template",
            TemplateName = "order_confirmation",
            LanguageCode = "en_US",
            Components =
            [
                new TemplateComponentInput
                {
                    Type = "body",
                    Parameters = [new TemplateParameterInput { Type = "text", Text = "Jane Doe" }],
                },
            ],
        };

        var result = await function.FunctionHandler(input, _context);

        Assert.True(result.Success);
        Assert.Equal("wamid.def456", result.MessageId);

        Assert.NotNull(capturedComponents);
        Assert.Single(capturedComponents!);
        Assert.Equal("body", capturedComponents![0].Type);
        Assert.Equal("Jane Doe", capturedComponents[0].Parameters[0].Text);
    }

    [Theory]
    [InlineData(null, "en_US")]
    [InlineData("order_confirmation", null)]
    public async Task FunctionHandler_WithTemplateMessage_MissingNameOrLanguage_ReturnsFailure(
        string? templateName, string? languageCode)
    {
        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput
            {
                To = "+15551234567",
                MessageType = "template",
                TemplateName = templateName,
                LanguageCode = languageCode,
            },
            _context);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task FunctionHandler_WithUnsupportedMessageType_ReturnsFailure()
    {
        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput { To = "+15551234567", MessageType = "image" },
            _context);

        Assert.False(result.Success);
        Assert.Contains("image", result.ErrorMessage);
    }

    [Fact]
    public async Task FunctionHandler_WhenMessageServiceThrowsWhatsAppMessageException_ReturnsFailure()
    {
        _messageService
            .Setup(s => s.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppMessageException("boom", new InvalidOperationException("inner")));

        var function = CreateFunction();

        var result = await function.FunctionHandler(
            new SendMessageInput { To = "+15551234567", MessageType = "text", Text = "Hello!" },
            _context);

        Assert.False(result.Success);
        Assert.Equal("boom", result.ErrorMessage);
    }
}
