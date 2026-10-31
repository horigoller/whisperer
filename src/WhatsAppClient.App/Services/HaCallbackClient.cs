using System.Net.Http.Json;

namespace WhatsAppClient.App.Services;

/// <summary>Posts a tapped-button callback to a Home Assistant webhook.</summary>
public interface IHaCallbackClient
{
    /// <summary>POSTs <paramref name="payload"/> as JSON to <paramref name="url"/>; returns true on 2xx.</summary>
    Task<bool> PostAsync(string url, object payload, CancellationToken ct = default);
}

/// <summary>
/// Minimal HTTPS JSON poster for Home Assistant webhooks. Uses a shared <see cref="HttpClient"/>
/// with a short timeout; never throws — a failed/timed-out callback just returns false.
/// </summary>
public sealed class HaCallbackClient : IHaCallbackClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<bool> PostAsync(string url, object payload, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync(url, payload, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
