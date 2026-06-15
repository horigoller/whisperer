using Microsoft.Extensions.Options;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Models;

namespace WhatsAppClient.Api;

public static class Security
{
    private const string ItemKey = "session-user";

    /// <summary>The authenticated user for the current request (set by <see cref="AuthFilter"/>).</summary>
    public static SessionUser CurrentUser(this HttpContext ctx) => (SessionUser)ctx.Items[ItemKey]!;

    /// <summary>Endpoint filter requiring a valid session (Bearer header or `session` cookie).</summary>
    public static async ValueTask<object?> AuthFilter(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var tokens = http.RequestServices.GetRequiredService<ISessionTokenService>();

        var token = ExtractToken(http);
        var user = token is null ? null : await tokens.ValidateAsync(token, http.RequestAborted);
        if (user is null) return Results.Json(new { error = "unauthenticated" }, statusCode: 401);

        http.Items[ItemKey] = user;
        return await next(ctx);
    }

    /// <summary>Endpoint filter requiring an admin. Use after <see cref="AuthFilter"/>.</summary>
    public static async ValueTask<object?> AdminFilter(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (ctx.HttpContext.CurrentUser().Role != UserRole.Admin)
            return Results.Json(new { error = "forbidden" }, statusCode: 403);
        return await next(ctx);
    }

    /// <summary>Endpoint filter for the machine notify API: requires the configured X-Api-Key.</summary>
    public static async ValueTask<object?> ApiKeyFilter(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var configured = http.RequestServices.GetRequiredService<IOptions<AppOptions>>().Value.NotifyApiKey;
        if (string.IsNullOrEmpty(configured))
            return Results.Json(new { error = "notify api disabled" }, statusCode: 503);

        var provided = http.Request.Headers["X-Api-Key"].ToString();
        // Length-aware constant-time compare to avoid leaking the key via timing.
        if (provided.Length != configured.Length ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(provided), System.Text.Encoding.UTF8.GetBytes(configured)))
            return Results.Json(new { error = "unauthenticated" }, statusCode: 401);

        return await next(ctx);
    }

    private static string? ExtractToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.Ordinal)) return header["Bearer ".Length..];
        if (http.Request.Cookies.TryGetValue("session", out var c)) return c;
        // Lets <img>/<video src> authenticate (they can't send a Bearer header), like the WS handshake.
        var q = http.Request.Query["token"].ToString();
        return string.IsNullOrEmpty(q) ? null : q;
    }
}
