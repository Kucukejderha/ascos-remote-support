using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace RemoteSupport.SessionAgent;

public sealed class HostSession
{
    public HostSession(string deviceId, string sessionId, string code, string accessToken) { DeviceId=deviceId; SessionId=sessionId; Code=code; AccessToken=accessToken; }
    public string DeviceId { get; }
    public string SessionId { get; }
    public string Code { get; }
    public string AccessToken { get; }
}

public sealed class SignalingHostClient : IDisposable
{
    private readonly Uri _baseUri;
    private readonly ECDsa _identity;
    private readonly HttpClient _http;
    private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

    public SignalingHostClient(Uri baseUri, ECDsa identity)
    {
        _baseUri = baseUri; _identity = identity;
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<HostSession> CreateSessionAsync(string displayName, CancellationToken token)
    {
        var registration = await PostAsync<RegistrationResponse>("/v1/devices", new RegisterDeviceRequest { PublicKeySpkiBase64=Convert.ToBase64String(ExportPublicKeySpki()), DisplayName=displayName }, token);
        var challenge = await PostAsync<ChallengeResponse>($"/v1/devices/{registration.DeviceId}/challenge", new EmptyRequest(), token);
        var signature = _identity.SignData(Convert.FromBase64String(challenge.NonceBase64), HashAlgorithmName.SHA256);
        var access = await PostAsync<AccessResponse>($"/v1/devices/{registration.DeviceId}/verify", new VerifyRequest { ChallengeId=challenge.ChallengeId, SignatureBase64=Convert.ToBase64String(signature) }, token);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        var code = await PostAsync<SupportCodeResponse>("/v1/support-codes", new EmptyRequest(), token);
        return new HostSession(registration.DeviceId, code.SessionId, code.Code, access.AccessToken);
    }

    public async Task<ClientWebSocket> ConnectHostSocketAsync(HostSession session, CancellationToken token)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {session.AccessToken}");
        var builder = new UriBuilder(_baseUri) { Scheme=_baseUri.Scheme=="https"?"wss":"ws", Path=$"/v1/sessions/{session.SessionId}/signal", Query="role=host" };
        try { await socket.ConnectAsync(builder.Uri, token); return socket; } catch { socket.Dispose(); throw; }
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken token)
    {
        using var content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, token);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        return _json.Deserialize<T>(text) ?? throw new InvalidDataException("Sunucu boş yanıt döndürdü.");
    }

    private byte[] ExportPublicKeySpki()
    {
        var cng = _identity as ECDsaCng ?? throw new PlatformNotSupportedException("Windows ECDSA anahtarı gerekli.");
        var blob = cng.Key.Export(CngKeyBlobFormat.EccPublicBlob);
        if (blob.Length != 72 || BitConverter.ToInt32(blob,4) != 32) throw new CryptographicException("Beklenmeyen P-256 anahtarı.");
        byte[] prefix={0x30,0x59,0x30,0x13,0x06,0x07,0x2A,0x86,0x48,0xCE,0x3D,0x02,0x01,0x06,0x08,0x2A,0x86,0x48,0xCE,0x3D,0x03,0x01,0x07,0x03,0x42,0x00,0x04};
        var spki=new byte[91]; Buffer.BlockCopy(prefix,0,spki,0,prefix.Length); Buffer.BlockCopy(blob,8,spki,prefix.Length,64); return spki;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class RegisterDeviceRequest { public string PublicKeySpkiBase64 { get; set; } = ""; public string DisplayName { get; set; } = ""; }
internal sealed class RegistrationResponse { public string DeviceId { get; set; } = ""; }
internal sealed class EmptyRequest { }
internal sealed class ChallengeResponse { public string ChallengeId { get; set; } = ""; public string NonceBase64 { get; set; } = ""; }
internal sealed class VerifyRequest { public string ChallengeId { get; set; } = ""; public string SignatureBase64 { get; set; } = ""; }
internal sealed class AccessResponse { public string AccessToken { get; set; } = ""; }
internal sealed class SupportCodeResponse { public string SessionId { get; set; } = ""; public string Code { get; set; } = ""; }
