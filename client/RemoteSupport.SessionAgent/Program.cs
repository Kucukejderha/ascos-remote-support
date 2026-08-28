using System.Net;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    static Program()
    {
        // Registered before Main is JIT-compiled so embedded assemblies resolve
        // even when Main itself references types from the embedded DLLs.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedProtocol;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        RegisterCrashLogging();
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        WaitForPredecessor(args);
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            AppDiagnostics.Write("Second RotaLink launch rejected; activating the existing window.");
            SingleInstanceGuard.ActivateExistingWindow();
            return;
        }
        EnablePhysicalPixelCoordinates();
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        string identityName;
        using (var currentIdentity = WindowsIdentity.GetCurrent()) identityName = currentIdentity.Name;
        var elevated = IsProcessElevated();
        AppDiagnostics.Write("RotaLink v" + version + " started in the interactive user session on " + Environment.OSVersion +
            ". Session=" + Process.GetCurrentProcess().SessionId + ", Identity=" + identityName +
            ", Elevated=" + elevated + ", DpiMode=" + DpiMode + ", Bitness=" + (Environment.Is64BitProcess ? "x64" : "x86") + ".");
        IDisposable? helperRuntime = SystemBrokerService.TryStart();
        if (helperRuntime is null) helperRuntime = ElevatedSessionHelper.TryStart();
        using var runtime = helperRuntime;
        var serverArgument = args.FirstOrDefault(a => a != "--wait" && !int.TryParse(a, out _));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(serverArgument, elevated, runtime is not null));
    }

    private static string DpiMode = "unaware";

    /// <summary>
    /// A self-update relaunch waits for the predecessor instance to release the
    /// single-instance mutex before acquiring it, so the swap never leaves the
    /// agent closed.
    /// </summary>
    private static void WaitForPredecessor(string[] args)
    {
        var pid = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--wait" && int.TryParse(args[i + 1], out pid)) break;
        }
        if (pid <= 0) return;
        try
        {
            using var predecessor = Process.GetProcessById(pid);
            if (predecessor.WaitForExit(15000))
                AppDiagnostics.Write("Self-update: predecessor process " + pid + " exited; starting as the single instance.");
            else
                AppDiagnostics.Write("Self-update: predecessor process " + pid + " did not exit within 15 seconds; proceeding anyway.");
        }
        catch (ArgumentException)
        {
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Self-update: waiting for the predecessor failed.", exception);
        }
    }

    private static void RegisterCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            try { AppDiagnostics.Write("Unhandled exception: " + eventArgs.ExceptionObject); } catch { }
        };
        Application.ThreadException += (sender, eventArgs) =>
        {
            try { AppDiagnostics.Write("Thread exception: " + eventArgs.Exception); } catch { }
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    }

    /// <summary>
    /// Makes the process DPI-aware so GDI capture and injected coordinates use
    /// the same physical pixel space. PER_MONITOR_AWARE_V2 (-4) requires 1703+;
    /// PER_MONITOR_AWARE (-3) works on Windows 8.1/Server 2016 and is enough.
    /// </summary>
    private static void EnablePhysicalPixelCoordinates()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(new IntPtr(-4))) { DpiMode = "per-monitor-v2"; return; }
            if (SetProcessDpiAwarenessContext(new IntPtr(-3))) { DpiMode = "per-monitor"; return; }
        }
        catch (EntryPointNotFoundException) { }
        DpiMode = SetProcessDPIAware() ? "system-aware" : "unaware";
    }

    /// <summary>
    /// Loads embedded runtime DLLs (the common protocol project and its
    /// System.Memory dependencies) so the portable client stays a single
    /// executable without side-by-side DLL files.
    /// </summary>
    private static readonly string[] EmbeddedAssemblyNames =
    {
        "RemoteSupport.Protocol", "System.Memory", "System.Buffers",
        "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors"
    };

    private static Assembly? ResolveEmbeddedProtocol(object sender, ResolveEventArgs args)
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

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var size = Marshal.SizeOf(typeof(int));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            return GetTokenInformation(identity.Token, 20, buffer, size, out _) && Marshal.ReadInt32(buffer) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int informationLength, out int returnLength);
}
