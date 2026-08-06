using System.Net.Http.Headers;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using RemoteSupport.Protocol;
using RemoteSupport.Service;
using RemoteSupport.SessionAgent;

await VerifyIpcAsync();
await VerifyDeviceIdentityAsync();
VerifyCaptureAndConsentGate();
VerifyFrameEncoder();
if (args is ["--codec-only"])
{
    Console.WriteLine("Local codec and security smoke checks passed.");
    return;
}

var baseUri = new Uri(args.FirstOrDefault() ?? "http://127.0.0.1:5188");
using var http = new HttpClient { BaseAddress = baseUri };
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

var registration = await PostAsync<Registration>("/v1/devices", new
{
    publicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
    displayName = "Integration Host"
});
var challenge = await PostAsync<Challenge>($"/v1/devices/{registration.DeviceId}/challenge", new { });
var signature = key.SignData(Convert.FromBase64String(challenge.NonceBase64), HashAlgorithmName.SHA256);
var access = await PostAsync<Access>($"/v1/devices/{registration.DeviceId}/verify", new
{
    challengeId = challenge.ChallengeId,
    signatureBase64 = Convert.ToBase64String(signature)
});

http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
var support = await PostAsync<SupportCode>("/v1/support-codes", new { });
http.DefaultRequestHeaders.Authorization = null;
var guest = await PostAsync<GuestSession>("/v1/support-codes/redeem", new { code = support.Code });

using var hostSocket = CreateSocket(access.AccessToken);
using var guestSocket = new ClientWebSocket();
guestSocket.Options.AddSubProtocol($"ascos.guest.{guest.GuestToken}");
var wsBase = new UriBuilder(baseUri) { Scheme = baseUri.Scheme == "https" ? "wss" : "ws" }.Uri;
await hostSocket.ConnectAsync(new Uri(wsBase, $"/v1/sessions/{support.SessionId}/signal?role=host"), CancellationToken.None);
await guestSocket.ConnectAsync(new Uri(wsBase, $"/v1/sessions/{support.SessionId}/signal?role=guest"), CancellationToken.None);

var sent = Encoding.UTF8.GetBytes("{\"type\":\"offer\",\"sdp\":\"smoke-test\"}");
await hostSocket.SendAsync(sent, WebSocketMessageType.Text, true, CancellationToken.None);
var received = new byte[1024];
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var result = await guestSocket.ReceiveAsync(received, timeout.Token);
var relayed = Encoding.UTF8.GetString(received, 0, result.Count);
if (relayed != Encoding.UTF8.GetString(sent)) throw new InvalidOperationException("Relayed payload differs.");

var relayPixels = new byte[960 * 540 * 4];
RandomNumberGenerator.Fill(relayPixels);
var frame = new ScreenFrameEncoder().Encode(new CapturedFrame(960, 540, relayPixels))
    ?? throw new InvalidOperationException("Relay key frame was skipped.");
await hostSocket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
var frameReceived = new byte[frame.Length];
var offset = 0;
do
{
    var part = await guestSocket.ReceiveAsync(frameReceived.AsMemory(offset), timeout.Token);
    offset += part.Count;
    if (part.EndOfMessage) break;
} while (offset < frameReceived.Length);
if (offset != frame.Length || !CryptographicOperations.FixedTimeEquals(frame, frameReceived))
    throw new InvalidOperationException("Large binary frame relay failed.");

Console.WriteLine(JsonSerializer.Serialize(new { registration.DeviceId, support.SessionId, CodeLength = support.Code.Length, Relayed = true, FrameBytes = frame.Length }));

static async Task VerifyIpcAsync()
{
    var key = RandomNumberGenerator.GetBytes(IpcAuthentication.SessionKeyBytes);
    var sessionId = Guid.NewGuid();
    var envelope = IpcAuthentication.Create(MessageKind.Heartbeat, sessionId, 1, "ready"u8, key);
    await using var stream = new MemoryStream();
    await IpcFraming.WriteAsync(stream, envelope, CancellationToken.None);
    stream.Position = 0;
    var decoded = await IpcFraming.ReadAsync(stream, CancellationToken.None);
    if (!IpcAuthentication.Verify(decoded, key)) throw new InvalidOperationException("IPC authentication failed.");
    var sequences = new SequenceGuard();
    if (!sequences.TryAccept(sessionId, decoded.Sequence) || sequences.TryAccept(sessionId, decoded.Sequence))
        throw new InvalidOperationException("IPC replay protection failed.");

    var pipeName = $"ascos.remote-support.smoke.{Guid.NewGuid():N}";
    await using var server = NamedPipeTransport.CreateCurrentUserServer(pipeName);
    await using var client = NamedPipeTransport.CreateCurrentUserClient(pipeName);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var acceptTask = server.WaitForConnectionAsync(timeout.Token);
    await client.ConnectAsync(timeout.Token);
    await acceptTask;
    await IpcFraming.WriteAsync(client, envelope with { Sequence = 2, AuthenticationTag = IpcAuthentication.Create(MessageKind.Heartbeat, sessionId, 2, "ready"u8, key).AuthenticationTag }, timeout.Token);
    var piped = await IpcFraming.ReadAsync(server, timeout.Token);
    if (!IpcAuthentication.Verify(piped, key) || !sequences.TryAccept(sessionId, piped.Sequence))
        throw new InvalidOperationException("Named Pipe authentication failed.");
}

static async Task VerifyDeviceIdentityAsync()
{
    if (!OperatingSystem.IsWindows()) return;
    var directory = Path.Combine(Path.GetTempPath(), "ascos-remote-support-smoke", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "identity.json");
    try
    {
        var store = new DeviceIdentityStore(path);
        using var created = await store.LoadOrCreateAsync(CancellationToken.None);
        using var loaded = await store.LoadOrCreateAsync(CancellationToken.None);
        if (created.DeviceId != loaded.DeviceId || created.PublicKeySpkiBase64 != loaded.PublicKeySpkiBase64)
            throw new InvalidOperationException("DPAPI device identity did not persist consistently.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void VerifyCaptureAndConsentGate()
{
    if (!OperatingSystem.IsWindows()) return;
    try
    {
        using var capture = new GdiScreenCapture(64, 36);
        var frame = capture.Capture();
        if (frame.Pixels.Length != 64 * 36 * 4) throw new InvalidOperationException("GDI capture size is invalid.");
    }
    catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
    {
        Console.WriteLine("GDI capture check skipped: test desktop access denied.");
    }
    var sessionId = Guid.NewGuid();
    var consent = new ConsentStateMachine(TimeProvider.System);
    consent.Request(sessionId, TimeSpan.FromMinutes(1));
    var dispatcher = new WindowsInputDispatcher(consent, sessionId);
    if (dispatcher.TryDispatch("{\"type\":\"move\",\"x\":1,\"y\":1}"u8))
        throw new InvalidOperationException("Input was accepted before local consent.");
}

static void VerifyFrameEncoder()
{
    const int width = 320, height = 180;
    var firstPixels = new byte[width * height * 4];
    for (var i = 0; i < firstPixels.Length; i += 4)
    {
        firstPixels[i] = 28;
        firstPixels[i + 1] = 35;
        firstPixels[i + 2] = 48;
    }

    var encoder = new ScreenFrameEncoder();
    var keyFrame = encoder.Encode(new CapturedFrame(width, height, firstPixels))
        ?? throw new InvalidOperationException("Initial key frame was skipped.");
    if (keyFrame[0] != ScreenFrameProtocol.JpegFrame || keyFrame[5] != 0xFF || keyFrame[6] != 0xD8)
        throw new InvalidOperationException("Initial JPEG frame is invalid.");
    if (encoder.Encode(new CapturedFrame(width, height, firstPixels.ToArray())) is not null)
        throw new InvalidOperationException("Unchanged frame was not skipped.");

    var secondPixels = firstPixels.ToArray();
    secondPixels.AsSpan(1000, 4000).Fill(0xA5);
    var deltaFrame = encoder.Encode(new CapturedFrame(width, height, secondPixels))
        ?? throw new InvalidOperationException("Changed frame was skipped.");
    if (deltaFrame[0] != ScreenFrameProtocol.JpegFrame || deltaFrame[5] != 0xFF || deltaFrame[6] != 0xD8)
        throw new InvalidOperationException("Changed JPEG frame is invalid.");
    if (keyFrame.Length >= firstPixels.Length / 3 || deltaFrame.Length >= secondPixels.Length / 3)
        throw new InvalidOperationException("JPEG compression is unexpectedly poor.");
    Console.WriteLine($"Codec smoke: raw={firstPixels.Length}, jpeg={keyFrame.Length}, changed={deltaFrame.Length}, jpegRatio={(double)keyFrame.Length / firstPixels.Length:P2}");
}

ClientWebSocket CreateSocket(string token)
{
    var socket = new ClientWebSocket();
    socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
    return socket;
}

async Task<T> PostAsync<T>(string path, object body)
{
    using var response = await http.PostAsJsonAsync(path, body);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("Empty response.");
}

internal sealed record Registration(string DeviceId);
internal sealed record Challenge(string ChallengeId, string NonceBase64);
internal sealed record Access(string AccessToken);
internal sealed record SupportCode(string SessionId, string Code);
internal sealed record GuestSession(string GuestToken);
