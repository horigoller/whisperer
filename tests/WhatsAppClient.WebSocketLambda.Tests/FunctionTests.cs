using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Tests;
using WhatsAppClient.WebSocketLambda;
using Xunit;

namespace WhatsAppClient.WebSocketLambda.Tests;

public class FunctionTests
{
    private sealed class FixedSecret : ISessionSecretProvider
    {
        public Task<string> GetSecretAsync(CancellationToken ct = default) =>
            Task.FromResult("a-very-long-test-signing-secret-of-many-bytes");
    }

    private readonly InMemoryAppRepository _repo = new();
    private readonly SessionTokenService _tokens = new(new FixedSecret());
    private readonly TestLambdaContext _context = new();

    private Function CreateFunction() => new(_repo, _tokens);

    [Fact]
    public async Task Authorize_ValidToken_AllowsWithUsername()
    {
        var token = await _tokens.IssueAsync(new SessionUser("hori", UserRole.Admin, "Hori"));
        var req = new APIGatewayCustomAuthorizerRequest
        {
            MethodArn = "arn:aws:execute-api:us-east-1:1:abc/wss/$connect",
            QueryStringParameters = new Dictionary<string, string> { ["token"] = token },
        };

        var resp = await CreateFunction().Authorize(req, _context);

        Assert.Equal("Allow", resp.PolicyDocument.Statement[0].Effect);
        Assert.Equal("hori", resp.PrincipalID);
    }

    [Fact]
    public async Task Authorize_BadOrMissingToken_Denies()
    {
        var req = new APIGatewayCustomAuthorizerRequest
        {
            MethodArn = "arn:aws:execute-api:us-east-1:1:abc/wss/$connect",
            QueryStringParameters = new Dictionary<string, string> { ["token"] = "not-a-jwt" },
        };

        var resp = await CreateFunction().Authorize(req, _context);
        Assert.Equal("Deny", resp.PolicyDocument.Statement[0].Effect);
    }

    [Fact]
    public async Task Handle_Connect_StoresConnection_Disconnect_Removes()
    {
        var connect = new APIGatewayProxyRequest
        {
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext { ConnectionId = "c1", RouteKey = "$connect" },
        };
        await CreateFunction().Handle(connect, _context);
        Assert.True(_repo.Connections.ContainsKey("c1"));

        var disconnect = new APIGatewayProxyRequest
        {
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext { ConnectionId = "c1", RouteKey = "$disconnect" },
        };
        await CreateFunction().Handle(disconnect, _context);
        Assert.False(_repo.Connections.ContainsKey("c1"));
    }
}
