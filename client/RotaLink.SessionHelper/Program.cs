using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

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
            var uiAccess = ReadCurrentUiAccess();
            log.Write("Session helper started. Session=" + sessionId + ", Identity=" + identity.Name +
                ", UIAccess=" + uiAccess + ", WTSState=" + ReadSessionState(sessionId) + ".");
            if (!identity.IsSystem)
                throw new InvalidOperationException("Session helper must run with the LocalSystem identity.");

            var clientProcessId = ParseClientProcessId(args);
            log.Write("Session helper is restricted to RotaLink client process " + clientProcessId + ".");

            using var windowStation = InteractiveWindowStation.Attach();
            log.Write("Session helper attached to interactive window station WinSta0.");

            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                "Global\\RotaLink.SessionHelper.Stop." + sessionId);
            using var engine = new InputEngine(log);
            var server = new InputPipeServer(sessionId, clientProcessId, engine, log);
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

    private static uint ParseClientProcessId(string[] args)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, "--client-pid", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !uint.TryParse(args[index + 1], out var processId) || processId == 0)
            throw new ArgumentException("--client-pid <id> is required.");
        return processId;
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

    private static string ReadSessionState(uint sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, 8, out var buffer, out var bytes) || bytes < sizeof(int))
            return "Unknown(" + Marshal.GetLastWin32Error() + ")";
        try { return ((WtsConnectState)Marshal.ReadInt32(buffer)).ToString(); }
        finally { WTSFreeMemory(buffer); }
    }

    private enum WtsConnectState { Active, Connected, ConnectQuery, Shadow, Disconnected, Idle, Listen, Reset, Down, Init }

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
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}

internal sealed class HelperLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public HelperLog(uint sessionId)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var directory = identity.IsSystem
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
