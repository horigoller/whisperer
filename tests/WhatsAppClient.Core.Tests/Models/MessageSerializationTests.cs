using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppClient.Core.Models;
using Xunit;

namespace WhatsAppClient.Core.Tests.Models;

public class MessageSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void TextMessage_SerializesToWhatsAppCloudApiShape()
    {
        var message = new WhatsAppTextMessage
        {
            To = "+15551234567",
            Text = new WhatsAppTextBody { Body = "Hello!", PreviewUrl = true },
        };

        var json = Serialize(message);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("+15551234567", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());

        var text = root.GetProperty("text");
        Assert.Equal("Hello!", text.GetProperty("body").GetString());
        Assert.True(text.GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public void TextMessage_DefaultsPreviewUrlToFalse()
    {
        var message = new WhatsAppTextMessage
        {
            To = "+15551234567",
            Text = new WhatsAppTextBody { Body = "Hello!" },
        };

        using var document = JsonDocument.Parse(Serialize(message));

        Assert.False(document.RootElement.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public void TemplateMessage_SerializesToWhatsAppCloudApiShape()
    {
        var message = new WhatsAppTemplateMessage
        {
            To = "+15551234567",
            Template = new WhatsAppTemplate
            {
                Name = "order_confirmation",
                Language = new WhatsAppTemplateLanguage { Code = "en_US" },
                Components =
                [
                    new WhatsAppTemplateComponent
                    {
                        Type = "body",
                        Parameters =
                        [
                            WhatsAppTemplateParameter.FromText("Jane Doe"),
                            WhatsAppTemplateParameter.FromText("12345"),
                        ],
                    },
                ],
            },
        };

        using var document = JsonDocument.Parse(Serialize(message));
        var root = document.RootElement;

        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("order_confirmation", template.GetProperty("name").GetString());
        Assert.Equal("en_US", template.GetProperty("language").GetProperty("code").GetString());

        var components = template.GetProperty("components");
        Assert.Equal(1, components.GetArrayLength());

        var bodyComponent = components[0];
        Assert.Equal("body", bodyComponent.GetProperty("type").GetString());
        Assert.False(bodyComponent.TryGetProperty("sub_type", out _));
        Assert.False(bodyComponent.TryGetProperty("index", out _));

        var parameters = bodyComponent.GetProperty("parameters");
        Assert.Equal(2, parameters.GetArrayLength());
        Assert.Equal("text", parameters[0].GetProperty("type").GetString());
        Assert.Equal("Jane Doe", parameters[0].GetProperty("text").GetString());
        Assert.Equal("12345", parameters[1].GetProperty("text").GetString());
    }

    [Fact]
    public void TemplateMessage_WithoutComponents_OmitsComponentsProperty()
    {
        var message = new WhatsAppTemplateMessage
        {
            To = "+15551234567",
            Template = new WhatsAppTemplate
            {
                Name = "hello_world",
                Language = new WhatsAppTemplateLanguage { Code = "en_US" },
            },
        };

        using var document = JsonDocument.Parse(Serialize(message));

        Assert.False(document.RootElement.GetProperty("template").TryGetProperty("components", out _));
    }

    [Fact]
    public void TemplateMessage_WithButtonComponent_SerializesSubTypeAndIndex()
    {
        var message = new WhatsAppTemplateMessage
        {
            To = "+15551234567",
            Template = new WhatsAppTemplate
            {
                Name = "order_confirmation",
                Language = new WhatsAppTemplateLanguage { Code = "en_US" },
                Components =
                [
                    new WhatsAppTemplateComponent
                    {
                        Type = "button",
                        SubType = "url",
                        Index = "0",
                        Parameters = [WhatsAppTemplateParameter.FromText("abc123")],
                    },
                ],
            },
        };

        using var document = JsonDocument.Parse(Serialize(message));
        var component = document.RootElement.GetProperty("template").GetProperty("components")[0];

        Assert.Equal("button", component.GetProperty("type").GetString());
        Assert.Equal("url", component.GetProperty("sub_type").GetString());
        Assert.Equal("0", component.GetProperty("index").GetString());
    }

    private static string Serialize(WhatsAppMessage message) =>
        JsonSerializer.Serialize(message, message.GetType(), SerializerOptions);
}
