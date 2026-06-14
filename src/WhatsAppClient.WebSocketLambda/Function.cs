using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Persistence;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace WhatsAppClient.WebSocketLambda;

/// <summary>
/// Backs the WebSocket API: a REQUEST authorizer that validates the session JWT from the query
/// string, and a connection handler that tracks $connect/$disconnect in the app table.
/// </summary>
public sealed class Function
{
    private readonly IAppRepository _repo;
    private readonly ISessionTokenService _tokens;

    public Function()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.SectionName));
        services.TryAddAWSService<IAmazonDynamoDB>();
        services.TryAddAWSService<IAmazonSecretsManager>();
        services.AddSingleton<IAppRepository, DynamoAppRepository>();
        services.AddSingleton<ISessionSecretProvider, SessionSecretProvider>();
        services.AddSingleton<ISessionTokenService, SessionTokenService>();

        var provider = services.BuildServiceProvider();
        _repo = provider.GetRequiredService<IAppRepository>();
        _tokens = provider.GetRequiredService<ISessionTokenService>();
    }

    internal Function(IAppRepository repo, ISessionTokenService tokens)
    {
        _repo = repo;
        _tokens = tokens;
    }

    /// <summary>$connect authorizer: validate ?token= and allow/deny, passing the username through.</summary>
    public async Task<APIGatewayCustomAuthorizerResponse> Authorize(
        APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
    {
        string? token = null;
        request.QueryStringParameters?.TryGetValue("token", out token);
        var user = string.IsNullOrEmpty(token) ? null : await _tokens.ValidateAsync(token);

        return new APIGatewayCustomAuthorizerResponse
        {
            PrincipalID = user?.Username ?? "anonymous",
            PolicyDocument = new APIGatewayCustomAuthorizerPolicy
            {
                Version = "2012-10-17",
                Statement =
                [
                    new APIGatewayCustomAuthorizerPolicy.IAMPolicyStatement
                    {
                        Effect = user is null ? "Deny" : "Allow",
                        Action = new HashSet<string> { "execute-api:Invoke" },
                        Resource = new HashSet<string> { request.MethodArn },
                    },
                ],
            },
            Context = new APIGatewayCustomAuthorizerContextOutput { ["username"] = user?.Username ?? string.Empty },
        };
    }

    /// <summary>Connection lifecycle: store on $connect, remove on $disconnect.</summary>
    public async Task<APIGatewayProxyResponse> Handle(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var rc = request.RequestContext;
        var connectionId = rc.ConnectionId;
        switch (rc.RouteKey)
        {
            case "$connect":
                await _repo.PutConnectionAsync(connectionId, ExtractUsername(rc));
                break;
            case "$disconnect":
                await _repo.DeleteConnectionAsync(connectionId);
                break;
        }
        return new APIGatewayProxyResponse { StatusCode = 200, Body = "ok" };
    }

    private static string ExtractUsername(APIGatewayProxyRequest.ProxyRequestContext rc)
    {
        try
        {
            var u = rc.Authorizer?["username"]?.ToString();
            return string.IsNullOrEmpty(u) ? "unknown" : u;
        }
        catch
        {
            return "unknown"; // username is cosmetic; broadcasting doesn't depend on it
        }
    }
}
