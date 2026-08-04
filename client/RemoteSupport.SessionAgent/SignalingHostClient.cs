using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;

namespace RemoteSupport.SessionAgent;

public sealed record HostSession(string DeviceId, string SessionId, string Code, string AccessToken);

public sealed class SignalingHostClient : IDisposable
{
    private readonly Uri _baseUri;
    private readonly ECDsa _identity;
    private readonly HttpClient _http;

    public SignalingHostClient(Uri baseUri, ECDsa identity)
    {
        _baseUri = baseUri;
        _identity = identity;
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<HostSession> CreateSessionAsync(string displayName, CancellationToken cancellationToken)
    {
        var registration = await PostAsync("/v1/devices",
            new RegisterDeviceRequest(Convert.ToBase64String(_identity.ExportSubjectPublicKeyInfo()), displayName),
            SessionAgentJsonContext.Default.RegisterDeviceRequest, SessionAgentJsonContext.Default.RegistrationResponse, cancellationToken);
        var challenge = await PostAsync($"/v1/devices/{registration.DeviceId}/challenge", new EmptyRequest(),
            SessionAgentJsonContext.Default.EmptyRequest, SessionAgentJsonContext.Default.ChallengeResponse, cancellationToken);
        var signature = _identity.SignData(Convert.FromBase64String(challenge.NonceBase64), HashAlgorithmName.SHA256);
        var access = await PostAsync($"/v1/devices/{registration.DeviceId}/verify",
            new VerifyRequest(challenge.ChallengeId, Convert.ToBase64String(signature)),
            SessionAgentJsonContext.Default.VerifyRequest, SessionAgentJsonContext.Default.AccessResponse, cancellationToken);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        var code = await PostAsync("/v1/support-codes", new EmptyRequest(),
            SessionAgentJsonContext.Default.EmptyRequest, SessionAgentJsonContext.Default.SupportCodeResponse, cancellationToken);
        return new(registration.DeviceId, code.SessionId, code.Code, access.AccessToken);
    }

    public async Task<ClientWebSocket> ConnectHostSocketAsync(HostSession session, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {session.AccessToken}");
        var builder = new UriBuilder(_baseUri) { Scheme = _baseUri.Scheme == "https" ? "wss" : "ws", Path = $"/v1/sessions/{session.SessionId}/signal", Query = "role=host" };
        try { await socket.ConnectAsync(builder.Uri, cancellationToken); return socket; }
        catch { socket.Dispose(); throw; }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body,
        JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, requestType, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(responseType, cancellationToken) ?? throw new InvalidDataException("Empty server response.");
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record RegisterDeviceRequest(string PublicKeySpkiBase64, string DisplayName);
internal sealed record RegistrationResponse(string DeviceId);
internal sealed record EmptyRequest;
internal sealed record ChallengeResponse(string ChallengeId, string NonceBase64);
internal sealed record VerifyRequest(string ChallengeId, string SignatureBase64);
internal sealed record AccessResponse(string AccessToken);
internal sealed record SupportCodeResponse(string SessionId, string Code);
