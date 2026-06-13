using System.Text.Json.Serialization;
using Amazon.Lambda.SQSEvents;

namespace WhatsAppClient.ReceiveLambda;

/// <summary>
/// Source-generated JSON serialization context for the Lambda's input (<see cref="SQSEvent"/>)
/// and output (<see cref="SQSBatchResponse"/>) types, for fast cold starts.
/// </summary>
[JsonSerializable(typeof(SQSEvent))]
[JsonSerializable(typeof(SQSBatchResponse))]
public partial class LambdaJsonSerializerContext : JsonSerializerContext
{
}
