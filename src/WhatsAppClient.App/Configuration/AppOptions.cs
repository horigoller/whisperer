using System.ComponentModel.DataAnnotations;

namespace WhatsAppClient.App.Configuration;

/// <summary>
/// Configuration for the management app, bound from the "App" section (env vars prefixed
/// <c>App__</c> on Lambda).
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>The single-table DynamoDB table for app data (users, contacts, conversations, messages).</summary>
    [Required]
    public required string TableName { get; set; }

    /// <summary>S3 bucket holding the built React SPA, served by the API Lambda.</summary>
    public string WebBucketName { get; set; } = string.Empty;

    /// <summary>Secrets Manager ARN of the JWT signing secret. Empty falls back to <see cref="SessionSecret"/>.</summary>
    public string SessionSecretArn { get; set; } = string.Empty;

    /// <summary>
    /// Fallback signing secret for local/dev when <see cref="SessionSecretArn"/> is empty. Must be
    /// at least 32 bytes for HS256.
    /// </summary>
    public string SessionSecret { get; set; } = "dev-insecure-session-secret-change-me-please";

    /// <summary>WABA id, used to list approved templates.</summary>
    public string WabaId { get; set; } = string.Empty;

    /// <summary>Bootstrap admin seeded on first login when no users exist.</summary>
    public string BootstrapAdminUsername { get; set; } = string.Empty;

    public string BootstrapAdminPhone { get; set; } = string.Empty;

    /// <summary>AUTHENTICATION template used to deliver login codes outside the 24h window.</summary>
    public string LoginTemplateName { get; set; } = "login_code";

    /// <summary>
    /// HTTPS management endpoint of the WebSocket API (https://{id}.execute-api.{region}.amazonaws.com/{stage})
    /// used to push events to connected clients. Empty disables real-time push (no-op).
    /// </summary>
    public string RealtimeEndpoint { get; set; } = string.Empty;

    /// <summary>The wss:// URL the browser connects to, handed to the client after login.</summary>
    public string RealtimeWsUrl { get; set; } = string.Empty;
}
