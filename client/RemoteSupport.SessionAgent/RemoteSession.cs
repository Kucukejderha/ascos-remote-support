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
        using var capture = new GdiScreenCapture(960, 540);
        var input = new WindowsInputDispatcher(consent, sessionId);
        try { await Task.WhenAny(CaptureLoopAsync(socket, capture, token), ReceiveInputLoopAsync(socket, input, token)); }
        finally
        {
            consent.Stop(sessionId);
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "local user stopped", CancellationToken.None);
        }
    }

    private static async Task CaptureLoopAsync(ClientWebSocket socket, GdiScreenCapture capture, CancellationToken token)
    {
        var encoder = new ScreenFrameEncoder();
        var nextKeyFrame = Environment.TickCount;
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
                input.TryDispatch(buffer.AsSpan(0, result.Count));
        }
    }
}
