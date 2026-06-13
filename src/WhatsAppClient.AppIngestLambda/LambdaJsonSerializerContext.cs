using System.Text.Json.Serialization;
using WhatsAppClient.App.Ingest;

namespace WhatsAppClient.AppIngestLambda;

/// <summary>
/// Source-generated JSON context for the handler input. Case-insensitive so the PascalCase
/// EventBridge detail keys bind (the nested message/status use Core's snake_case attributes).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppInboundEvent))]
public partial class LambdaJsonSerializerContext : JsonSerializerContext
{
}
