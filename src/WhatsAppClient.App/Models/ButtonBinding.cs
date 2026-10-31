namespace WhatsAppClient.App.Models;

/// <summary>
/// Binds an actionable alert's quick-reply buttons to a Home Assistant callback. Created when a
/// <c>home_action</c> alert is sent; resolved when the recipient taps "Handle It" or "Ignore".
/// The button payloads encode this binding's <see cref="Token"/> (<c>H:{token}</c> / <c>I:{token}</c>).
/// </summary>
public sealed class ButtonBinding
{
    /// <summary>Opaque id tying the alert to this binding (the outbound message id).</summary>
    public required string Token { get; init; }

    /// <summary>The recipient's WhatsApp id (phone without a leading "+").</summary>
    public required string WaId { get; init; }

    /// <summary>The home name the alert came from (body variable {{1}}), relayed to HA.</summary>
    public string? Home { get; init; }

    /// <summary>HTTPS Home Assistant webhook POSTed when "Handle It" is tapped in time.</summary>
    public required string CallbackUrl { get; init; }

    /// <summary>Opaque action data relayed to HA so the automation knows what to do.</summary>
    public string? Data { get; init; }

    /// <summary>active | consumed | ignored. Only an <c>active</c>, unexpired binding fires.</summary>
    public required string Status { get; set; }

    /// <summary>Unix seconds after which "Handle It" no longer fires (send time + 5 min).</summary>
    public required long ExpiresAt { get; init; }

    /// <summary>Unix seconds for DynamoDB TTL cleanup (well past <see cref="ExpiresAt"/>).</summary>
    public required long Ttl { get; init; }
}
