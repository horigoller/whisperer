using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.S3;
using WhatsAppClient.Api;
using WhatsAppClient.App;
using WhatsAppClient.App.Auth;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Models;
using WhatsAppClient.App.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWhatsAppApp(builder.Configuration);
builder.Services.TryAddAWSService<IAmazonS3>();
builder.Services.AddSingleton<StaticSite>();

// Run as an AWS Lambda behind a Function URL (HTTP API v2 payload) when deployed.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

var api = app.MapGroup("/api");

// ---- Auth (unauthenticated) ------------------------------------------------
var auth = api.MapGroup("/auth");

auth.MapPost("/start", async (StartLoginRequest req, AuthService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Username)) return Results.BadRequest(new { error = "username required" });
    var challengeId = await svc.StartAsync(req.Username);
    return Results.Ok(new { challengeId });
});

auth.MapPost("/verify", async (VerifyRequest req, AuthService svc, ISessionTokenService tokens, IOptions<AppOptions> appOptions, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.ChallengeId) || string.IsNullOrWhiteSpace(req.Code))
        return Results.BadRequest(new { error = "challengeId and code required" });

    var user = await svc.VerifyAsync(req.ChallengeId, req.Code);
    if (user is null) return Results.BadRequest(new { error = "invalid or expired code" });

    var token = await tokens.IssueAsync(user);
    http.Response.Cookies.Append("session", token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(12),
    });
    return Results.Ok(new
    {
        token,
        wsUrl = appOptions.Value.RealtimeWsUrl,
        user = new { username = user.Username, displayName = user.DisplayName, role = user.Role.ToString() },
    });
});

// Lets the login page show why a code didn't arrive (async delivery failures like 131037).
auth.MapGet("/delivery", async (string challengeId, AuthService svc) =>
{
    var d = await svc.GetCodeDeliveryAsync(challengeId);
    return Results.Ok(new { failed = d.Failed, errorCode = d.ErrorCode, errorDetail = d.ErrorDetail });
});

auth.MapPost("/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete("session");
    return Results.Ok(new { ok = true });
});

auth.MapGet("/me", (HttpContext http, IOptions<AppOptions> appOptions) =>
{
    var u = http.CurrentUser();
    return Results.Ok(new
    {
        wsUrl = appOptions.Value.RealtimeWsUrl,
        user = new { username = u.Username, displayName = u.DisplayName, role = u.Role.ToString() },
    });
}).AddEndpointFilter(Security.AuthFilter);

// ---- Conversations ---------------------------------------------------------
var convos = api.MapGroup("/conversations").AddEndpointFilter(Security.AuthFilter);

convos.MapGet("/", async (ConversationService svc) => Results.Ok(new { conversations = await svc.ListAsync() }));

convos.MapGet("/{waId}/messages", async (string waId, ConversationService svc, HttpContext http) =>
{
    // Incremental poll: ?after=<createdAt> returns only newer messages (no conversation/unread work).
    var after = http.Request.Query["after"].ToString();
    if (!string.IsNullOrEmpty(after))
    {
        return Results.Ok(new { messages = await svc.GetNewMessagesAsync(waId, after) });
    }
    var (messages, conversation) = await svc.GetThreadAsync(waId);
    return Results.Ok(new { messages, conversation });
});

convos.MapPost("/{waId}/reply", async (string waId, ReplyRequest req, ConversationService svc, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "text required" });
    try
    {
        var message = await svc.ReplyAsync(waId, req.Text, http.CurrentUser().Username);
        return Results.Ok(new { message });
    }
    catch (WindowClosedException)
    {
        return Results.Json(new { error = "window_closed", message = "The 24h window is closed; start a template exchange instead." }, statusCode: 409);
    }
    catch (ContactNotFoundException)
    {
        return Results.NotFound(new { error = "contact not found" });
    }
});

convos.MapPost("/{waId}/template", async (string waId, TemplateSendRequest req, ConversationService svc, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.TemplateName) || string.IsNullOrWhiteSpace(req.LanguageCode))
        return Results.BadRequest(new { error = "templateName and languageCode required" });
    try
    {
        var message = await svc.SendTemplateAsync(waId, req.TemplateName, req.LanguageCode, req.Params ?? [], http.CurrentUser().Username);
        return Results.Ok(new { message });
    }
    catch (ContactNotFoundException)
    {
        return Results.NotFound(new { error = "contact not found" });
    }
});

// ---- Contacts --------------------------------------------------------------
var contacts = api.MapGroup("/contacts").AddEndpointFilter(Security.AuthFilter);

contacts.MapGet("/", async (ContactService svc) => Results.Ok(new { contacts = await svc.ListAsync() }));

contacts.MapPost("/", async (AddContactRequest req, ContactService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.PhoneE164)) return Results.BadRequest(new { error = "phoneE164 required" });
    try
    {
        var contact = await svc.AddAsync(req.Name, req.PhoneE164);
        return Results.Ok(new { contact });
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new { error = "invalid phone number" });
    }
});

// ---- System users ----------------------------------------------------------
var users = api.MapGroup("/users").AddEndpointFilter(Security.AuthFilter);

users.MapGet("/", async (UserService svc) => Results.Ok(new { users = await svc.ListAsync() }));

users.MapPost("/", async (AddUserRequest req, UserService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.PhoneE164))
        return Results.BadRequest(new { error = "username and phoneE164 required" });
    try
    {
        var role = string.Equals(req.Role, "admin", StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.Agent;
        var user = await svc.AddAsync(req.Username, req.DisplayName, req.PhoneE164, role);
        return Results.Ok(new { user });
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new { error = "invalid phone number" });
    }
}).AddEndpointFilter(Security.AdminFilter);

users.MapDelete("/{username}", async (string username, UserService svc, HttpContext http) =>
{
    if (string.Equals(username, http.CurrentUser().Username, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "cannot delete yourself" });
    await svc.DeleteAsync(username);
    return Results.Ok(new { ok = true });
}).AddEndpointFilter(Security.AdminFilter);

// ---- Templates -------------------------------------------------------------
api.MapGet("/templates", async (TemplateService svc) => Results.Ok(new { templates = await svc.ListApprovedAsync() }))
    .AddEndpointFilter(Security.AuthFilter);

api.MapGet("/health", () => Results.Ok(new { ok = true }));

// Unmatched /api routes are JSON 404s, never the SPA.
api.MapFallback(() => Results.Json(new { error = "not found" }, statusCode: 404));

// ---- SPA (everything else) -------------------------------------------------
// Explicit catch-all pattern: the parameterless MapFallback uses "{*path:nonfile}", whose
// :nonfile constraint excludes file-like paths (e.g. /assets/app.js) so they'd 404 instead of
// being served from S3. "/{**path}" matches every unrouted path, files included.
app.MapFallback("/{**path}", async (HttpContext http, StaticSite site) =>
{
    var (status, body, type) = await site.ServeAsync(http.Request.Path, http.RequestAborted);
    http.Response.StatusCode = status;
    http.Response.ContentType = type;
    await http.Response.Body.WriteAsync(body);
});

app.Run();

// Exposed for integration testing via WebApplicationFactory.
public partial class Program;
