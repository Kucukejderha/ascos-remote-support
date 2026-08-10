using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RotaLink.SessionHelper;

internal static class Program
{
    public static int Main(string[] args)
    {
        var sessionId = ParseSessionId(args);
        if (!ProcessIdToSessionId((uint)System.Diagnostics.Process.GetCurrentProcess().Id, out var actualSessionId) || actualSessionId != sessionId)
            return 11;

        var log = new HelperLog(sessionId);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            log.Write("Session helper started. Session=" + sessionId + ", Identity=" + identity.Name + ".");
            if (!identity.IsSystem) log.Write("WARNING: helper is not running as LocalSystem; secure-desktop input will be unavailable.");

            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                "Global\\RotaLink.SessionHelper.Stop." + sessionId);
            using var engine = new InputEngine(log);
            var server = new InputPipeServer(sessionId, engine, log);
            server.Run(stop);
            return 0;
        }
        catch (Exception exception)
        {
            log.Write("Fatal helper error: " + exception);
            return 1;
        }
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
