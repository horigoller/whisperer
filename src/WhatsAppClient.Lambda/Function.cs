using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppClient.Core;
using WhatsAppClient.Core.Exceptions;
using WhatsAppClient.Core.Models;
using WhatsAppClient.Core.Services;
using WhatsAppClient.Lambda.Models;

namespace WhatsAppClient.Lambda;

/// <summary>
/// Lambda function that sends a single WhatsApp message via AWS End User Messaging Social.
/// </summary>
public sealed class Function
{
    private readonly IWhatsAppMessageService _messageService;

    /// <summary>
    /// Used by the Lambda runtime. Builds the dependency graph from configuration
    /// (appsettings.json + environment variables) and the default AWS credential chain.
    /// </summary>
    public Function()
        : this(BuildMessageService())
    {
    }

    /// <summary>
    /// Used by tests to inject a mock <see cref="IWhatsAppMessageService"/>.
    /// </summary>
    internal Function(IWhatsAppMessageService messageService)
    {
        _messageService = messageService;
    }

    /// <summary>
    /// The Lambda entry point. Sends the message described by <paramref name="input"/> and
    /// reports the outcome instead of throwing, so that callers (e.g. Step Functions, SQS
    /// consumers) can branch on <see cref="SendMessageOutput.Success"/>.
    /// </summary>
    public async Task<SendMessageOutput> FunctionHandler(SendMessageInput input, ILambdaContext context)
    {
        try
        {
            var result = await SendAsync(input, context.Logger).ConfigureAwait(false);
            return new SendMessageOutput { Success = true, MessageId = result.MessageId };
        }
        catch (Exception ex) when (ex is ArgumentException or WhatsAppMessageException)
        {
            context.Logger.LogError($"Failed to send WhatsApp message: {ex.Message}");
            return new SendMessageOutput { Success = false, ErrorMessage = ex.Message };
        }
    }

    private Task<SendWhatsAppMessageResult> SendAsync(SendMessageInput input, ILambdaLogger logger)
    {
        switch (input.MessageType.ToLowerInvariant())
        {
            case "text":
                if (string.IsNullOrEmpty(input.Text))
                {
                    throw new ArgumentException("'text' is required when messageType is 'text'.", nameof(input));
                }

                logger.LogInformation($"Sending text message to {input.To}");
                return _messageService.SendTextMessageAsync(input.To, input.Text, input.PreviewUrl);

            case "template":
                if (string.IsNullOrWhiteSpace(input.TemplateName) || string.IsNullOrWhiteSpace(input.LanguageCode))
                {
                    throw new ArgumentException(
                        "'templateName' and 'languageCode' are required when messageType is 'template'.", nameof(input));
                }

                logger.LogInformation($"Sending template '{input.TemplateName}' message to {input.To}");
                return _messageService.SendTemplateMessageAsync(
                    input.To, input.TemplateName, input.LanguageCode, MapComponents(input.Components));

            default:
                throw new ArgumentException(
                    $"Unsupported messageType '{input.MessageType}'. Expected 'text' or 'template'.", nameof(input));
        }
    }

    private static IReadOnlyList<WhatsAppTemplateComponent>? MapComponents(List<TemplateComponentInput>? components)
    {
        if (components is null)
        {
            return null;
        }

        return components
            .Select(component => new WhatsAppTemplateComponent
            {
                Type = component.Type,
                SubType = component.SubType,
                Index = component.Index,
                Parameters = component.Parameters
                    .Select(parameter => WhatsAppTemplateParameter.FromText(parameter.Text ?? string.Empty))
                    .ToList(),
            })
            .ToList();
    }

    private static IWhatsAppMessageService BuildMessageService()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddWhatsAppMessaging(configuration);

        return services.BuildServiceProvider().GetRequiredService<IWhatsAppMessageService>();
    }
}
