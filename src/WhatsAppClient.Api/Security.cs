using WhatsAppClient.App.Auth;
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

    private static string? ExtractToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.Ordinal)) return header["Bearer ".Length..];
        return http.Request.Cookies.TryGetValue("session", out var c) ? c : null;
    }
}
