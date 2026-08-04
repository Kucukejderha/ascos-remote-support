using System.Net.WebSockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using RemoteSupport.Signaling;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SecurityStore>();
builder.Services.AddSingleton<SessionBroker>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The app port is reachable only from the private Compose network; Caddy owns the public ports.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var app = builder.Build();
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ascos-remote-support-signaling" }));
app.MapGet("/operator", () => Results.Content(OperatorPage.Html, "text/html; charset=utf-8"));

app.MapPost("/v1/devices", (RegisterDeviceRequest request, SecurityStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest();
    try { return Results.Ok(new RegisterDeviceResponse(store.Register(request.PublicKeySpkiBase64, request.DisplayName))); }
    catch (ArgumentException) { return Results.BadRequest(); }
}).RequireRateLimiting("authentication");

app.MapPost("/v1/devices/{deviceId}/challenge", (string deviceId, SecurityStore store) =>
{
    try { return Results.Ok(store.CreateChallenge(deviceId)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
}).RequireRateLimiting("authentication");

app.MapPost("/v1/devices/{deviceId}/verify", (string deviceId, VerifyChallengeRequest request, SecurityStore store) =>
{
    try { return Results.Ok(store.Verify(deviceId, request.ChallengeId, request.SignatureBase64)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
}).RequireRateLimiting("authentication");

app.MapPost("/v1/support-codes", async (HttpContext context, SecurityStore store, AuditLog audit) =>
{
    if (!store.TryAuthenticate(context.Request.Headers.Authorization, out var deviceId)) return Results.Unauthorized();
    var created = store.CreateCode(deviceId);
    await audit.WriteAsync("support_code_created", deviceId, created.SessionId, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
    return Results.Ok(created);
});

app.MapPost("/v1/support-codes/redeem", async (HttpContext context, RedeemSupportCodeRequest request, SecurityStore store, AuditLog audit) =>
{
    try
    {
        var redeemed = store.Redeem(request.Code);
        await audit.WriteAsync("support_code_redeemed", redeemed.HostDeviceId, redeemed.SessionId, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
        return Results.Ok(redeemed);
    }
    catch (UnauthorizedAccessException)
    {
        await audit.WriteAsync("support_code_redeem_failed", null, null, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
        return Results.Unauthorized();
    }
}).RequireRateLimiting("authentication");

app.Map("/v1/sessions/{sessionId}/signal", async (HttpContext context, string sessionId, SecurityStore store, SessionBroker broker, AuditLog audit) =>
{
    var role = context.Request.Query["role"].ToString();
    var requestedProtocol = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
    var authorized = role == "guest"
        ? store.TryAuthorizeGuestProtocol(sessionId, requestedProtocol) || store.TryAuthorizeSession(sessionId, role, context.Request.Headers.Authorization)
        : store.TryAuthorizeSession(sessionId, role, context.Request.Headers.Authorization);
    if (!context.WebSockets.IsWebSocketRequest || (role != "host" && role != "guest") || !authorized)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync(role == "guest" && requestedProtocol is not null ? requestedProtocol : null);
    if (!broker.TryAttach(sessionId, role, socket))
    {
        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Role already connected", context.RequestAborted);
        return;
    }
    if (role == "host") store.MarkHostConnected(sessionId);

    await audit.WriteAsync("session_peer_connected", role == "host" ? "host" : null, sessionId, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);

    try
    {
        var buffer = new byte[1024 * 1024 + 64];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (!result.EndOfMessage || result.Count == 0)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Invalid signaling message", context.RequestAborted);
                break;
            }

            var peer = broker.GetPeer(sessionId, role);
            if (peer is not { State: WebSocketState.Open }) continue;
            await peer.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, true, context.RequestAborted);
        }
    }
    finally
    {
        broker.Detach(sessionId, role, socket);
        if (role == "host") store.EndSession(sessionId);
        await audit.WriteAsync("session_peer_disconnected", role == "host" ? "host" : null, sessionId, context.Connection.RemoteIpAddress?.ToString(), CancellationToken.None);
    }
});

app.Run();

public partial class Program;
