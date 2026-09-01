using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Web.Script.Serialization;

namespace RemoteSupport.SessionAgent;

internal static class RemoteSession
{
    public static async Task RunAsync(SignalingHostClient api, HostSession session, CancellationToken token)
    {
        var consent = new ConsentStateMachine();
        var sessionId = Guid.ParseExact(session.SessionId, "N");
        consent.Request(sessionId, TimeSpan.FromHours(8));
        consent.Decide(sessionId, approved: true);
        using var controlSocket = await api.ConnectHostSocketAsync(session, "control", token).ConfigureAwait(false);
        using var videoSocket = await api.ConnectHostSocketAsync(session, "video", token).ConfigureAwait(false);
        AppDiagnostics.Write("Host control and video WebSockets connected.");
        using var input = new WindowsInputDispatcher(consent, sessionId);
        try
        {
            var completed = await Task.WhenAny(
                CaptureLoopAsync(videoSocket, controlSocket, token),
                ReceiveInputLoopAsync(controlSocket, input, token)).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
        finally
        {
            consent.Stop(sessionId);
            foreach (var socket in new[] { controlSocket, videoSocket })
            {
                if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) continue;
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "local user stopped",
                        closeTimeout.Token).ConfigureAwait(false);
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

            if (controlSocket.State != WebSocketState.Closed) controlSocket.Abort();
            if (videoSocket.State != WebSocketState.Closed) videoSocket.Abort();
        }
    }

    private static async Task CaptureLoopAsync(ClientWebSocket socket, ClientWebSocket controlSocket, CancellationToken token)
    {
        using var native = SessionHelperVideoClient.TryConnect();
        if (native is not null)
        {
            var firstNativeFrame = true;
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var packet = await native.ReadWebSocketPacketAsync(token).ConfigureAwait(false);
                await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                if (firstNativeFrame)
                {
                    AppDiagnostics.Write("First DXGI/H.264 frame sent. Bytes=" + packet.Length + ".");
                    firstNativeFrame = false;
                }
            }
            return;
        }

        AppDiagnostics.Write("Privileged DXGI capture is unavailable; portable GDI fallback is active.");
        var encoder = new ScreenFrameEncoder();
        var nextKeyFrame = Environment.TickCount;
        var firstFrame = true;
        GdiScreenCapture? capture = null;
        var accessDeniedLogged = false;
        var captureStalled = false;
        var retryDelay = 750;
        while (socket.State == WebSocketState.Open)
        {
            try
            {
                capture ??= new GdiScreenCapture(960, 540);
                if (firstFrame) AppDiagnostics.Write("Screen capture initialized at 960x540.");
                await Task.Delay(70, token).ConfigureAwait(false);
                var frame = capture.Capture();
                accessDeniedLogged = false;
                retryDelay = 750;
                var now = Environment.TickCount;
                var forceKeyFrame = unchecked(now - nextKeyFrame) >= 0;
                var packet = encoder.Encode(frame, forceKeyFrame);
                if (packet is null) continue;
                if (forceKeyFrame) nextKeyFrame = now + 2_000;
                await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                if (captureStalled)
                {
                    captureStalled = false;
                    await SendCaptureStatusAsync(controlSocket, ok: true, token).ConfigureAwait(false);
                }
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
                if (!captureStalled)
                {
                    captureStalled = true;
                    await SendCaptureStatusAsync(controlSocket, ok: false, token).ConfigureAwait(false);
                }
                if (!accessDeniedLogged)
                {
                    AppDiagnostics.Write("Screen capture is temporarily unavailable (secure desktop or desktop transition). The session remains connected and capture will retry.", ex);
                    accessDeniedLogged = true;
                }
                await Task.Delay(retryDelay, token).ConfigureAwait(false);
                retryDelay = Math.Min(retryDelay * 2, 5000);
            }
        }
        capture?.Dispose();
    }

    private static async Task SendCaptureStatusAsync(ClientWebSocket controlSocket, bool ok, CancellationToken token)
    {
        try
        {
            if (controlSocket.State != WebSocketState.Open) return;
            var payload = Encoding.UTF8.GetBytes("{\"type\":\"capture-status\",\"ok\":" + (ok ? "true" : "false") + "}");
            await controlSocket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        catch
        {
            // Status reporting must never break the capture loop.
        }
    }

    private static async Task ReceiveInputLoopAsync(ClientWebSocket socket, WindowsInputDispatcher input, CancellationToken token)
    {
        var buffer = new byte[4096];
        string? lastReportedResult = null;
        var serializer = new JavaScriptSerializer();
        var lastSlowLogTick = 0;
        AppDiagnostics.Write("Input receive loop running on thread " + Environment.CurrentManagedThreadId + ".");
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
            {
                var dispatchStopwatch = Stopwatch.StartNew();
                var report = input.TryDispatchDetailed(buffer, result.Count);
                dispatchStopwatch.Stop();
                var dispatchMillis = dispatchStopwatch.ElapsedMilliseconds;
                if (dispatchMillis > 3000)
                {
                    StallsInWindow++;
                    AppDiagnostics.Write("Input dispatch stall detected. ElapsedMs=" + dispatchMillis +
                        ", TotalStalls=" + StallsInWindow + ", Thread=" + Environment.CurrentManagedThreadId + ".");
                }
                else if (dispatchMillis > 500 &&
                         unchecked(Environment.TickCount - lastSlowLogTick) > 2000)
                {
                    lastSlowLogTick = Environment.TickCount;
                    AppDiagnostics.Write("Slow input dispatch. ElapsedMs=" + dispatchMillis +
                        ", Thread=" + Environment.CurrentManagedThreadId + ".");
                }
                var acknowledgementText = serializer.Serialize(new
                {
                    type = "control-result",
                    ok = report.Accepted,
                    stage = report.Stage,
                    error = report.ErrorCode,
                    desktop = report.Desktop,
                    eventType = report.EventType
                });
                if (!report.Accepted || !string.Equals(acknowledgementText, lastReportedResult, StringComparison.Ordinal))
                {
                    var acknowledgement = Encoding.UTF8.GetBytes(acknowledgementText);
                    await socket.SendAsync(new ArraySegment<byte>(acknowledgement), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                    AppDiagnostics.Write("Remote input result reported. Accepted=" + report.Accepted +
                        ", Stage=" + report.Stage + ", Error=" + report.ErrorCode +
                        ", Desktop=" + report.Desktop + ", Event=" + report.EventType +
                        ", DispatchMs=" + dispatchMillis);
                    lastReportedResult = acknowledgementText;
                }
            }
        }
    }

    private static int StallsInWindow;

}
