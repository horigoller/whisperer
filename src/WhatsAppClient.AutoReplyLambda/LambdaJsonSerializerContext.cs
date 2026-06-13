using System.Text.Json.Serialization;
using WhatsAppClient.AutoReplyLambda.Models;

namespace WhatsAppClient.AutoReplyLambda;

/// <summary>
/// Source-generated JSON context for the handler's input/output. Case-insensitive so the
/// PascalCase EventBridge detail keys (e.g. "ContactName", "Message") bind cleanly.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AutoReplyEvent))]
[JsonSerializable(typeof(AutoReplyResult))]
public partial class LambdaJsonSerializerContext : JsonSerializerContext
{
}
