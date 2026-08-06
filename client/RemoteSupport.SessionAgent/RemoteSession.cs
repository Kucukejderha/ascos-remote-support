using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace RemoteSupport.SessionAgent;

internal static class RemoteSession
{
    public static async Task RunAsync(SignalingHostClient api, HostSession session, CancellationToken token)
    {
        var consent = new ConsentStateMachine();
        var sessionId = Guid.ParseExact(session.SessionId, "N");
        consent.Request(sessionId, TimeSpan.FromHours(8));
        consent.Decide(sessionId, approved: true);
        using var socket = await api.ConnectHostSocketAsync(session, token);
        AppDiagnostics.Write("Host WebSocket connected.");
        using var input = new WindowsInputDispatcher(consent, sessionId);
        using var sendGate = new SemaphoreSlim(1, 1);
        try
        {
            var completed = await Task.WhenAny(
                CaptureLoopAsync(socket, sendGate, token),
                ReceiveInputLoopAsync(socket, input, sendGate, token));
            await completed;
        }
        finally
        {
            consent.Stop(sessionId);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "local user stopped",
                        closeTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    AppDiagnostics.Write("WebSocket close handshake timed out; aborting the socket.");
                }
                catch (WebSocketException ex)
                {
                    AppDiagnostics.Write("WebSocket close handshake failed; aborting the socket.", ex);
                }
            }

            if (socket.State != WebSocketState.Closed)
                socket.Abort();
        }
    }

    private static async Task CaptureLoopAsync(ClientWebSocket socket, SemaphoreSlim sendGate, CancellationToken token)
    {
        var encoder = new ScreenFrameEncoder();
        var nextKeyFrame = Environment.TickCount;
        var firstFrame = true;
        GdiScreenCapture? capture = null;
        var accessDeniedLogged = false;
        while (socket.State == WebSocketState.Open)
        {
            try
            {
                capture ??= new GdiScreenCapture(960, 540);
                if (firstFrame) AppDiagnostics.Write("Screen capture initialized at 960x540.");
                await Task.Delay(70, token);
                var frame = capture.Capture();
                accessDeniedLogged = false;
                var now = Environment.TickCount;
                var forceKeyFrame = unchecked(now - nextKeyFrame) >= 0;
                var packet = encoder.Encode(frame, forceKeyFrame);
                if (packet is null) continue;
                if (forceKeyFrame) nextKeyFrame = now + 2_000;
                await SendAsync(socket, packet, WebSocketMessageType.Binary, sendGate, token);
                if (firstFrame)
                {
                    AppDiagnostics.Write("First screen frame sent. Bytes=" + packet.Length + ", Type=" + packet[0]);
                    firstFrame = false;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                capture?.Dispose();
                capture = null;
                if (!accessDeniedLogged)
                {
                    AppDiagnostics.Write("Screen capture is temporarily unavailable (secure desktop or desktop transition). The session remains connected and capture will retry.", ex);
                    accessDeniedLogged = true;
                }
                await Task.Delay(750, token);
            }
        }
        capture?.Dispose();
    }

    private static async Task ReceiveInputLoopAsync(ClientWebSocket socket, WindowsInputDispatcher input, SemaphoreSlim sendGate, CancellationToken token)
    {
        var buffer = new byte[4096];
        var resultReported = false;
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
            {
                var accepted = input.TryDispatch(buffer, result.Count);
                if (!resultReported)
                {
                    var acknowledgement = Encoding.UTF8.GetBytes("{\"type\":\"control-result\",\"ok\":" + (accepted ? "true" : "false") + "}");
                    await SendAsync(socket, acknowledgement, WebSocketMessageType.Text, sendGate, token);
                    AppDiagnostics.Write("First remote input result reported. Accepted=" + accepted);
                    resultReported = true;
                }
            }
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, byte[] payload, WebSocketMessageType type, SemaphoreSlim gate, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { await socket.SendAsync(new ArraySegment<byte>(payload), type, true, token); }
        finally { gate.Release(); }
    }
}
