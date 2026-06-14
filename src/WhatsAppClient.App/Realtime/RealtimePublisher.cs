using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Persistence;

namespace WhatsAppClient.App.Realtime;

/// <summary>A real-time event pushed to connected console clients.</summary>
public sealed record RealtimeEvent(
    string Type,           // "message" | "status"
    string WaId,
    object? Message = null,
    string? MessageId = null,
    string? Status = null,
    int? ErrorCode = null,
    string? ErrorDetail = null);

public interface IRealtimePublisher
{
    Task PublishAsync(RealtimeEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Broadcasts events to every connected WebSocket client via the API Gateway @connections API,
/// pruning connections that have gone away. No-op when <see cref="AppOptions.RealtimeEndpoint"/>
/// is unset (local/tests), so callers can publish unconditionally.
/// </summary>
public sealed class RealtimePublisher : IRealtimePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAppRepository _repo;
    private readonly ILogger<RealtimePublisher> _logger;
    private readonly IAmazonApiGatewayManagementApi? _client;

    public RealtimePublisher(IAppRepository repo, IOptions<AppOptions> options, ILogger<RealtimePublisher> logger)
        : this(repo, BuildClient(options.Value), logger)
    {
    }

    /// <summary>Test seam: inject the management client directly (null disables push).</summary>
    public RealtimePublisher(IAppRepository repo, IAmazonApiGatewayManagementApi? client, ILogger<RealtimePublisher> logger)
    {
        _repo = repo;
        _client = client;
        _logger = logger;
    }

    private static IAmazonApiGatewayManagementApi? BuildClient(AppOptions options) =>
        string.IsNullOrEmpty(options.RealtimeEndpoint)
            ? null
            : new AmazonApiGatewayManagementApiClient(
                new AmazonApiGatewayManagementApiConfig { ServiceURL = options.RealtimeEndpoint });

    public async Task PublishAsync(RealtimeEvent evt, CancellationToken ct = default)
    {
        var client = _client;
        if (client is null) return; // real-time disabled

        var payload = JsonSerializer.SerializeToUtf8Bytes(evt, JsonOptions);
        var connectionIds = await _repo.ListConnectionIdsAsync(ct).ConfigureAwait(false);

        foreach (var id in connectionIds)
        {
            try
            {
                using var stream = new MemoryStream(payload, writable: false);
                await client.PostToConnectionAsync(
                    new PostToConnectionRequest { ConnectionId = id, Data = stream }, ct).ConfigureAwait(false);
            }
            catch (GoneException)
            {
                await _repo.DeleteConnectionAsync(id, ct).ConfigureAwait(false); // stale; drop it
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push to connection {ConnectionId}", id);
            }
        }
    }
}
