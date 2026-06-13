using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WhatsAppClient.AutoReplyLambda.Configuration;
using WhatsAppClient.AutoReplyLambda.Models;
using WhatsAppClient.Core;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Services;

namespace WhatsAppClient.AutoReplyLambda;

/// <summary>
/// Triggered by an EventBridge rule on the inbound bus (source <c>whatsapp.inbound</c>,
/// detail-type <c>MessageReceived</c>). Sends a configurable acknowledgement back to the sender.
/// The reply is a text send, which only produces <c>StatusUpdated</c> events — never another
/// <c>MessageReceived</c> — so the rule cannot re-trigger itself.
/// </summary>
public sealed class Function
{
    private readonly IWhatsAppMessageService _messageService;
    private readonly string _replyMessage;

    /// <summary>Used by the Lambda runtime. Builds the dependency graph from configuration.</summary>
    public Function()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddWhatsAppMessaging(configuration);
        services.AddOptions<AutoReplyOptions>().Bind(configuration.GetSection(AutoReplyOptions.SectionName));

        var provider = services.BuildServiceProvider();
        _messageService = provider.GetRequiredService<IWhatsAppMessageService>();
        _replyMessage = provider.GetRequiredService<IOptions<AutoReplyOptions>>().Value.Message;
    }

    /// <summary>Used by tests to inject a mock service and reply text.</summary>
    internal Function(IWhatsAppMessageService messageService, string replyMessage)
    {
        _messageService = messageService;
        _replyMessage = replyMessage;
    }

    public async Task<AutoReplyResult> FunctionHandler(AutoReplyEvent input, ILambdaContext context)
    {
        var from = input.Detail?.Message?.From;
        if (string.IsNullOrWhiteSpace(from))
        {
            context.Logger.LogWarning("Auto-reply skipped: event had no sender ('from').");
            return new AutoReplyResult { Replied = false, ErrorMessage = "No sender in event." };
        }

        // Inbound 'from' is a wa_id (no leading '+'); the send API expects E.164.
        var recipient = from.StartsWith('+') ? from : $"+{from}";

        try
        {
            context.Logger.LogInformation($"Auto-replying to {recipient}");
            var result = await _messageService.SendTextMessageAsync(recipient, _replyMessage).ConfigureAwait(false);
            return new AutoReplyResult { Replied = true, MessageId = result.MessageId };
        }
        catch (Exception ex) when (ex is ArgumentException or WhatsAppMessageException)
        {
            context.Logger.LogError($"Auto-reply failed: {ex.Message}");
            return new AutoReplyResult { Replied = false, ErrorMessage = ex.Message };
        }
    }
}
