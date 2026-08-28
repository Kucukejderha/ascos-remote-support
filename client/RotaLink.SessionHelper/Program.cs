using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RotaLink.SessionHelper;

internal static class Program
{
    /// <summary>
    /// Loads the embedded RemoteSupport.Protocol.dll (and its System.Memory
    /// dependencies) so the helper stays a single executable without
    /// side-by-side DLL files.
    /// </summary>
    private static readonly string[] EmbeddedAssemblyNames =
    {
        "RemoteSupport.Protocol", "System.Memory", "System.Buffers",
        "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors"
    };

    static Program()
    {
        // Registered in the static constructor so it is active before Main is
        // JIT-compiled: the JIT resolves assembly references while compiling
        // Main, before the first statement of Main ever runs.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssemblies;
    }

    private static Assembly? ResolveEmbeddedAssemblies(object sender, ResolveEventArgs args)
    {
        try
        {
            var requested = new AssemblyName(args.Name).Name;
            if (requested is null || Array.IndexOf(EmbeddedAssemblyNames, requested) < 0) return null;
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RotaLink.Runtime." + requested + ".dll");
            if (stream is null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Assembly.Load(memory.ToArray());
        }
        catch
        {
            return null;
        }
    }

    public static int Main(string[] args)
    {
        var dpiMode = EnablePhysicalPixelCoordinates();
        var sessionId = ParseSessionId(args);
        if (!ProcessIdToSessionId((uint)System.Diagnostics.Process.GetCurrentProcess().Id, out var actualSessionId) || actualSessionId != sessionId)
        {
            WriteEarlyTrace("session mismatch: actual=" + actualSessionId + ", expected=" + sessionId);
            return 11;
        }

        var log = new HelperLog(sessionId);
        try
        {
            var hasVirtualMetrics = TryParseVirtualMetrics(args, out var virtualLeft, out var virtualTop, out var virtualWidth, out var virtualHeight);
            using var identity = WindowsIdentity.GetCurrent();
            var uiAccess = ReadCurrentUiAccess();
            log.Write("Session helper started. Session=" + sessionId + ", Identity=" + identity.Name +
                ", UIAccess=" + uiAccess + ", Integrity=" + ReadIntegrityLabel() + ", DpiMode=" + dpiMode + ".");
            // The helper must use the same physical pixel space as the DPI-aware
            // agent that captures the screen, otherwise injected coordinates drift.
            log.Write("Screen metrics: virtual origin=" + GetSystemMetrics(76) + "," + GetSystemMetrics(77) +
                ", size=" + GetSystemMetrics(78) + "x" + GetSystemMetrics(79) + ".");
            if (hasVirtualMetrics)
                log.Write("Using virtual metrics from the agent: " + virtualLeft + "," + virtualTop + " " + virtualWidth + "x" + virtualHeight + ".");
            // The helper is launched with the elevated RotaLink token, so its
            // SendInput calls pass UIPI without needing the UIAccess flag.
            // The flag is only reported for diagnostics.

            using var windowStation = InteractiveWindowStation.Attach();
            log.Write("Session helper attached to interactive window station WinSta0.");

            // Injected input does not reset the idle timer; keep the session
            // awake for the whole support lifetime (screen saver, lock, sleep).
            using var keepAlive = new SessionKeepAlive(log);

            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                "Global\\RotaLink.SessionHelper.Stop." + sessionId);
            using var engine = hasVirtualMetrics
                ? new InputEngine(log, new RemoteSupport.Protocol.CoordinateTransformationEngine(virtualLeft, virtualTop, virtualWidth, virtualHeight))
                : new InputEngine(log);
            using var bridge = new NativeCaptureBridge(sessionId, log);
            var server = new InputPipeServer(sessionId, engine, log);

            using var stopSource = new CancellationTokenSource();
            var stopWatcher = Task.Run(() => { stop.WaitOne(); stopSource.Cancel(); });
            var inputTask = Task.Run(() => server.Run(stop));
            var videoTask = bridge.RunAsync(stopSource.Token);

            Task.WaitAny(inputTask, videoTask);
            if (!inputTask.IsCompleted)
            {
                // The video task ended on its own (e.g. no native capture);
                // keep serving input until the stop event fires.
                log.Write("Video task ended; input remains active.");
                inputTask.Wait();
            }
            stopSource.Cancel();
            try { Task.WaitAll(inputTask, videoTask); }
            catch (AggregateException exception)
            {
                log.Write("Helper task stopped with errors: " + exception.Flatten());
            }
            return 0;
        }
        catch (Exception exception)
        {
            log.Write("Fatal helper error: " + exception);
            return 1;
        }
    }

    private static bool TryParseVirtualMetrics(string[] args, out int left, out int top, out int width, out int height)
    {
        left = top = width = height = 0;
        var index = Array.FindIndex(args, value => string.Equals(value, "--virtual", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;
        var parts = args[index + 1].Split(',');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out left) || !int.TryParse(parts[1], out top) ||
            !int.TryParse(parts[2], out width) || !int.TryParse(parts[3], out height)) return false;
        return width > 0 && height > 0;
    }

    private static string ReadIntegrityLabel()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token)) return "unknown";
            using (token)
            {
                GetTokenInformationBuffer(token, 25, IntPtr.Zero, 0, out var required);
                var buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformationBuffer(token, 25, buffer, required, out _)) return "unknown";
                    var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                    var binary = new byte[sid.BinaryLength];
                    sid.GetBinaryForm(binary, 0);
                    var rid = BitConverter.ToInt32(binary, binary.Length - 4);
                    return "0x" + rid.ToString("X4");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch
        {
            return "unknown";
        }
    }
    private static void WriteEarlyTrace(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RotaLink", "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "helper-early.log"),
                DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    private static string EnablePhysicalPixelCoordinates()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return "per-monitor-v2";
            if (SetProcessDpiAwarenessContext(new IntPtr(-3))) return "per-monitor";
        }
        catch (EntryPointNotFoundException) { }
        return SetProcessDPIAware() ? "system-aware" : "unaware";
    }

    private static uint ParseSessionId(string[] args)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, "--session", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !uint.TryParse(args[index + 1], out var sessionId))
            throw new ArgumentException("--session <id> is required.");
        return sessionId;
    }

    private static bool ReadCurrentUiAccess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken for UIAccess diagnostics failed.");
        using (token)
        {
            if (!GetTokenInformation(token, 26, out var uiAccess, sizeof(int), out var returnedLength))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation(TokenUIAccess) failed.");
            if (returnedLength != sizeof(int))
                throw new InvalidDataException("GetTokenInformation(TokenUIAccess) returned " + returnedLength + " bytes.");
            return uiAccess != 0;
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        out int information, int informationLength, out int returnLength);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationBuffer(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int informationLength, out int returnLength);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}

internal sealed class HelperLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public HelperLog(uint sessionId)
    {
        // SYSTEM helpers log under ProgramData (the service also writes there);
        // user-token helpers keep the per-user location.
        var directory = WindowsIdentity.GetCurrent().IsSystem
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RotaLink", "Logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotaLink");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "SessionHelper-" + sessionId + ".log");
    }

    public void Write(string message)
    {
        try
        {
            lock (_gate)
                File.AppendAllText(_path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never terminate the interactive input helper.
        }
    }
}
