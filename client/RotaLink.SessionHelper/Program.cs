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
                ", UIAccess=" + uiAccess + ".");
            if (!uiAccess) throw new InvalidOperationException("Interactive helper token is missing UIAccess.");

            using var windowStation = InteractiveWindowStation.Attach();
            log.Write("Session helper attached to interactive window station WinSta0.");

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
}

internal sealed class HelperLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public HelperLog(uint sessionId)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotaLink");
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
