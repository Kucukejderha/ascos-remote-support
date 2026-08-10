using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

[assembly: SupportedOSPlatform("windows")]

namespace RotaLink.SessionHelper;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 10;
        var sessionId = ParseSessionId(args);
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var actualSessionId) || actualSessionId != sessionId)
            return 11;

        var log = new HelperLog(sessionId);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            log.Write("Session helper started. Session=" + sessionId + ", Identity=" + identity.Name + ".");
            if (!identity.IsSystem) log.Write("WARNING: helper is not running as LocalSystem; secure-desktop input will be unavailable.");

            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                "Global\\RotaLink.SessionHelper.Stop." + sessionId);
            using var stopCts = new CancellationTokenSource();
            var stopTask = Task.Run(() =>
            {
                stop.WaitOne();
                stopCts.Cancel();
            });
            using var engine = new InputEngine(log);
            var server = new InputPipeServer(sessionId, engine, log);
            using var capture = new NativeCaptureBridge(sessionId, log);
            try
            {
                var captureTask = RunCaptureSafeAsync(capture, log, stopCts.Token);
                await server.RunAsync(stopCts.Token).ConfigureAwait(false);
                await captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopCts.IsCancellationRequested) { }
            stop.Set();
            await stopTask.ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            log.Write("Fatal helper error: " + exception);
            return 1;
        }
    }

    private static async Task RunCaptureSafeAsync(NativeCaptureBridge capture, HelperLog log, CancellationToken token)
    {
        try { await capture.RunAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { log.Write("DXGI capture bridge is unavailable; input helper remains active. " + exception); }
    }

    private static uint ParseSessionId(string[] args)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, "--session", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !uint.TryParse(args[index + 1], out var sessionId))
            throw new ArgumentException("--session <id> is required.");
        return sessionId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
}

internal sealed class HelperLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public HelperLog(uint sessionId)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RotaLink", "Logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "SessionHelper-" + sessionId + ".log");
    }

    public void Write(string message)
    {
        lock (_gate)
            File.AppendAllText(_path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
    }
}
