using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WhatsAppClient.App.Realtime;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class RealtimePublisherTests
{
    private readonly InMemoryAppRepository _repo = new();

    [Fact]
    public async Task PublishAsync_NoClient_IsNoOp()
    {
        _repo.Connections["c1"] = "u";
        var pub = new RealtimePublisher(_repo, client: null, NullLogger<RealtimePublisher>.Instance);

        // Should not throw and should leave connections untouched (real-time disabled).
        await pub.PublishAsync(new RealtimeEvent("message", "123"));
        Assert.True(_repo.Connections.ContainsKey("c1"));
    }

    [Fact]
    public async Task PublishAsync_BroadcastsToAll_AndPrunesGoneConnections()
    {
        _repo.Connections["good"] = "u";
        _repo.Connections["gone"] = "u";

        var client = new Mock<IAmazonApiGatewayManagementApi>();
        client.Setup(c => c.PostToConnectionAsync(It.Is<PostToConnectionRequest>(r => r.ConnectionId == "good"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostToConnectionResponse());
        client.Setup(c => c.PostToConnectionAsync(It.Is<PostToConnectionRequest>(r => r.ConnectionId == "gone"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoneException("gone"));

        var pub = new RealtimePublisher(_repo, client.Object, NullLogger<RealtimePublisher>.Instance);
        await pub.PublishAsync(new RealtimeEvent("message", "15551239999", Message: new { id = "m1" }));

        client.Verify(c => c.PostToConnectionAsync(It.IsAny<PostToConnectionRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.False(_repo.Connections.ContainsKey("gone")); // pruned
        Assert.True(_repo.Connections.ContainsKey("good"));
    }

    [Fact]
    public async Task Connections_RoundTrip()
    {
        await _repo.PutConnectionAsync("c1", "alice");
        await _repo.PutConnectionAsync("c2", "bob");
        Assert.Equal(2, (await _repo.ListConnectionIdsAsync()).Count);

        await _repo.DeleteConnectionAsync("c1");
        var ids = await _repo.ListConnectionIdsAsync();
        Assert.Single(ids);
        Assert.Equal("c2", ids[0]);
    }
}
