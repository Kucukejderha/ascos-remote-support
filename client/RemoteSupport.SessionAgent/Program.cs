using System.Net.WebSockets;
using System.Security.Cryptography;
using RemoteSupport.SessionAgent;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("ASCOS Remote Support Host\nUsage: ASCOS.RemoteSupport.Host.exe <https-server-url>\nA native consent dialog is shown before every session.");
    return 0;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("ASCOS Session Agent yalnızca Windows üzerinde çalışır.");
    return 2;
}

#if !PORTABLE_BUILD
if (args is ["--install"])
    return SelfInstaller.Install();
#endif

var server = new Uri(args.FirstOrDefault() ?? "https://45.87.173.201.nip.io");
using var identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var api = new SignalingHostClient(server, identity);

Console.Title = "ASCOS Uzaktan Destek";
Console.WriteLine("ASCOS Uzaktan Destek — görünür ve kullanıcı onaylı oturum");
Console.WriteLine($"Sunucu: {server}");
var session = await api.CreateSessionAsync(Environment.MachineName, CancellationToken.None);
Console.WriteLine($"\nCihaz ID : {session.DeviceId}");
Console.WriteLine($"Destek kodu: {session.Code}");
var operatorUri = new Uri(server, "/operator");
Console.WriteLine($"Operatör: {operatorUri}");
var approved = NativeConsentPrompt.Show(session.Code, operatorUri);
if (!approved)
{
    Console.WriteLine("Bağlantı reddedildi.");
    return 0;
}

var consent = new ConsentStateMachine(TimeProvider.System);
var sessionId = Guid.ParseExact(session.SessionId, "N");
consent.Request(sessionId, TimeSpan.FromMinutes(15));
consent.Decide(sessionId, approved: true);

using var socket = await api.ConnectHostSocketAsync(session, CancellationToken.None);
using var capture = new GdiScreenCapture(960, 540);
var input = new WindowsInputDispatcher(consent, sessionId);
using var cancellation = new CancellationTokenSource();
Console.WriteLine("\nOTURUM AKTİF — durdurmak için ENTER tuşuna basın.");

var captureTask = CaptureLoopAsync(socket, capture, cancellation.Token);
var inputTask = ReceiveInputLoopAsync(socket, input, cancellation.Token);
var localStopTask = Task.Run(Console.ReadLine);
var completedTask = await Task.WhenAny(localStopTask, captureTask, inputTask);
consent.Stop(sessionId);
cancellation.Cancel();
try { await Task.WhenAll(captureTask, inputTask); }
catch (OperationCanceledException) { }
catch (Exception) when (completedTask != localStopTask) { }
if (socket.State == WebSocketState.Open)
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "local user stopped", CancellationToken.None);
Console.WriteLine(completedTask == localStopTask
    ? "Oturum yerel kullanıcı tarafından sonlandırıldı."
    : "Sunucu bağlantısı kapandı. Eski destek kodu artık geçersiz; yeni kod için uygulamayı yeniden açın.");
return 0;

static async Task CaptureLoopAsync(ClientWebSocket socket, GdiScreenCapture capture, CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
    var encoder = new ScreenFrameEncoder();
    var nextKeyFrame = Environment.TickCount64;
    while (await timer.WaitForNextTickAsync(cancellationToken) && socket.State == WebSocketState.Open)
    {
        var frame = capture.Capture();
        var now = Environment.TickCount64;
        var forceKeyFrame = now >= nextKeyFrame;
        var packet = encoder.Encode(frame, forceKeyFrame);
        if (packet is null) continue;
        if (forceKeyFrame) nextKeyFrame = now + 2_000;
        await socket.SendAsync(packet, WebSocketMessageType.Binary, true, cancellationToken);
    }
}

static async Task ReceiveInputLoopAsync(ClientWebSocket socket, WindowsInputDispatcher input, CancellationToken cancellationToken)
{
    var buffer = new byte[4096];
    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close) return;
        if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) continue;
        input.TryDispatch(buffer.AsSpan(0, result.Count));
    }
}
