using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;

namespace WhatsAppClient.App.Auth;

public interface ISessionSecretProvider
{
    Task<string> GetSecretAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the JWT signing secret from Secrets Manager (cached for the container lifetime). Falls
/// back to <see cref="AppOptions.SessionSecret"/> when no ARN is configured (local/dev/tests).
/// </summary>
public sealed class SessionSecretProvider : ISessionSecretProvider
{
    private readonly IAmazonSecretsManager _secrets;
    private readonly AppOptions _options;
    private string? _cached;

    public SessionSecretProvider(IAmazonSecretsManager secrets, IOptions<AppOptions> options)
    {
        _secrets = secrets;
        _options = options.Value;
    }

    public async Task<string> GetSecretAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        if (string.IsNullOrEmpty(_options.SessionSecretArn))
        {
            _cached = _options.SessionSecret;
            return _cached;
        }
        var r = await _secrets.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = _options.SessionSecretArn }, ct);
        _cached = r.SecretString ?? string.Empty;
        return _cached;
    }
}
