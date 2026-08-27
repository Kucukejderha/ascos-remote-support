using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using RemoteSupport.Protocol;

namespace RotaLink.SessionHelper;

internal sealed class NativeCaptureBridge : IDisposable
{
    private readonly uint _sessionId;
    private readonly HelperLog _log;
    private Process? _capture;

    public NativeCaptureBridge(uint sessionId, HelperLog log)
    {
        _sessionId = sessionId;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "RotaLink.NativeCapture.exe");
        if (!File.Exists(executable))
        {
            _log.Write("Native capture executable is missing; DXGI/H.264 video capture is disabled.");
            return;
        }
        StartNativeCapture();
        SharedFrameReader frames;
        try
        {
            frames = SharedFrameReader.Open(_sessionId, TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch (Exception exception)
        {
            _log.Write("Native frame source unavailable; video capture is disabled: " + exception.Message);
            return;
        }
        using (frames)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = CreateVideoPipe();
                _log.Write("Waiting for video IPC client.");
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _log.Write("Video IPC client connected.");
                try { await PumpFramesAsync(frames, pipe, cancellationToken).ConfigureAwait(false); }
                catch (IOException exception) { _log.Write("Video IPC disconnected: " + exception.Message); }
            }
        }
    }

    private async Task PumpFramesAsync(SharedFrameReader reader, Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[VideoPacketCodec.HeaderBytes];
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await Task.Run(() => reader.ReadLatest(cancellationToken), cancellationToken).ConfigureAwait(false);
            var packetHeader = new VideoPacketHeader(VideoCodec.H264AnnexB, frame.KeyFrame, frame.Sequence,
                frame.Timestamp100Nanoseconds, frame.Width, frame.Height, frame.Payload.Length);
            VideoPacketCodec.WriteHeader(header, packetHeader);
            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(frame.Payload, 0, frame.Payload.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartNativeCapture()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "RotaLink.NativeCapture.exe");
        var capture = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--session " + _sessionId,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Native capture process could not be started.");
        _capture = capture;
        capture.EnableRaisingEvents = true;
        capture.Exited += (_, _) => _log.Write("Native capture exited with code " + capture.ExitCode + ".");
        _log.Write("Native DXGI capture started. Process=" + capture.Id + ".");
    }

    private NamedPipeServerStream CreateVideoPipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(GetInteractiveUserSid(),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        var pipe = new NamedPipeServerStream("Global\\RotaLink.SessionHelper." + _sessionId + ".Video.v1",
            PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            0, 64 * 1024);
        pipe.SetAccessControl(security);
        return pipe;
    }

    private SecurityIdentifier GetInteractiveUserSid()
    {
        if (!WTSQueryUserToken(_sessionId, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User ?? throw new InvalidOperationException("Interactive token has no user SID.");
    }

    public void Dispose()
    {
        if (_capture is null) return;
        try
        {
            if (!_capture.HasExited)
            {
                _capture.Kill();
                _capture.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException) { }
        finally { _capture.Dispose(); _capture = null; }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
}
