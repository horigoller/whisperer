using System.Text.Json.Serialization;
using WhatsAppClient.Lambda.Models;

namespace WhatsAppClient.Lambda;

/// <summary>
/// Source-generated JSON serialization context for the Lambda function's input and
/// output types, as recommended for .NET Lambda functions to avoid reflection-based
/// serialization at cold start.
/// </summary>
[JsonSerializable(typeof(SendMessageInput))]
[JsonSerializable(typeof(SendMessageOutput))]
public partial class LambdaJsonSerializerContext : JsonSerializerContext
{
}
