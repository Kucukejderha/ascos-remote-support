using System.Net.WebSockets;

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
        using var capture = new GdiScreenCapture(960, 540);
        AppDiagnostics.Write("Screen capture initialized at 960x540.");
        var input = new WindowsInputDispatcher(consent, sessionId);
        try
        {
            var completed = await Task.WhenAny(
                CaptureLoopAsync(socket, capture, token),
                ReceiveInputLoopAsync(socket, input, token));
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

    private static async Task CaptureLoopAsync(ClientWebSocket socket, GdiScreenCapture capture, CancellationToken token)
    {
        var encoder = new ScreenFrameEncoder();
        var nextKeyFrame = Environment.TickCount;
        var firstFrame = true;
        while (socket.State == WebSocketState.Open)
        {
            await Task.Delay(100, token);
            var frame = capture.Capture();
            var now = Environment.TickCount;
            var forceKeyFrame = unchecked(now - nextKeyFrame) >= 0;
            var packet = encoder.Encode(frame, forceKeyFrame);
            if (packet is null) continue;
            if (forceKeyFrame) nextKeyFrame = now + 2_000;
            await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, token);
            if (firstFrame)
            {
                AppDiagnostics.Write("First screen frame sent. Bytes=" + packet.Length + ", Type=" + packet[0]);
                firstFrame = false;
            }
        }
    }

    private static async Task ReceiveInputLoopAsync(ClientWebSocket socket, WindowsInputDispatcher input, CancellationToken token)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
                input.TryDispatch(buffer, result.Count);
        }
    }
}
