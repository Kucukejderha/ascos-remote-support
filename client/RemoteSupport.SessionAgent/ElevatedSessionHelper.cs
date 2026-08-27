using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteSupport.SessionAgent;

/// <summary>
/// Launches the session helper directly from the elevated RotaLink process.
/// The helper inherits the high-integrity token, so its SendInput calls are
/// not blocked by UIPI. This replaces the temporary SYSTEM service, which
/// could only mint a filtered (medium) interactive token whose UIAccess flag
/// is ineffective for unsigned executables.
/// </summary>
internal sealed class ElevatedSessionHelper : IDisposable
{
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceDelete = 0x00010000;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const uint StoppedStatus = 0x00000102;
    private const string StaleServiceName = "RotaLinkInputRuntime";

    private static int _isRunning;
    private readonly SafeProcessHandle _helperProcess;
    private readonly uint _sessionId;
    private readonly string _directory;
    private bool _disposed;

    public static bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    private ElevatedSessionHelper(SafeProcessHandle helperProcess, uint sessionId, string directory)
    {
        _helperProcess = helperProcess;
        _sessionId = sessionId;
        _directory = directory;
    }

    public static ElevatedSessionHelper? TryStart()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            if (assembly.GetManifestResourceInfo("RotaLink.Runtime.SessionHelper.exe") is null)
            {
                AppDiagnostics.Write("Session helper is not embedded; direct input mode remains active.");
                return null;
            }

            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RotaLink", "SessionRuntime", RuntimeVersion);
            Directory.CreateDirectory(directory);
            var helperPath = Path.Combine(directory, "RotaLink.SessionHelper.exe");
            var nativeCapturePath = Path.Combine(directory, "RotaLink.NativeCapture.exe");

            RemoveStaleService();
            Extract(assembly, "RotaLink.Runtime.SessionHelper.exe", helperPath);
            if (assembly.GetManifestResourceInfo("RotaLink.Runtime.NativeCapture.exe") is not null)
                Extract(assembly, "RotaLink.Runtime.NativeCapture.exe", nativeCapturePath);

            var sessionId = (uint)Process.GetCurrentProcess().SessionId;
            var helperProcess = LaunchHelper(helperPath, sessionId, out var processId);
            Volatile.Write(ref _isRunning, 1);
            AppDiagnostics.Write("Elevated SessionHelper started in session " + sessionId + ", process " + processId + ".");
            return new ElevatedSessionHelper(helperProcess, sessionId, directory);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Elevated session helper could not be started; control will fail honestly if SendInput is rejected.", exception);
            return null;
        }
    }

    private static SafeProcessHandle LaunchHelper(string helperPath, uint sessionId, out uint processId)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenDuplicate | TokenAssignPrimary | TokenQuery | TokenAdjustPrivileges, out var currentToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
        using (currentToken)
        {
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            if (!DuplicateTokenEx(currentToken, MaximumAllowed, IntPtr.Zero, 2, 1, out var elevatedToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed.");
            using (elevatedToken)
            {
                var environment = IntPtr.Zero;
                try
                {
                    if (!CreateEnvironmentBlock(out environment, elevatedToken, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEnvironmentBlock failed.");

                    var startup = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfo>(),
                        Desktop = "winsta0\\default"
                    };
                    var commandLine = "\"" + helperPath + "\" --service-child --session " + sessionId;
                    if (!CreateProcessAsUser(elevatedToken, helperPath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                            CreateUnicodeEnvironment | CreateNoWindow, environment, Path.GetDirectoryName(helperPath),
                            ref startup, out var processInformation))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed.");

                    using (var thread = new SafeKernelHandle(processInformation.Thread))
                    {
                        processId = processInformation.ProcessId;
                        return new SafeProcessHandle(processInformation.Process, true);
                    }
                }
                finally
                {
                    if (environment != IntPtr.Zero && !DestroyEnvironmentBlock(environment))
                        AppDiagnostics.Write("DestroyEnvironmentBlock failed with " + Marshal.GetLastWin32Error() + ".");
                }
            }
        }
    }

    private static void Extract(Assembly assembly, string resourceName, string destination)
    {
        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing runtime resource " + resourceName + ".");
        var temporary = destination + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(output);
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temporary, destination);
    }

    private static void RemoveStaleService()
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid) return;
        try
        {
            using var stale = OpenService(manager, StaleServiceName, ServiceQueryStatus | ServiceStop | ServiceDelete);
            if (stale.IsInvalid) return;
            var status = new ServiceStatus();
            ControlService(stale, ServiceControlStop, ref status);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (!QueryServiceStatus(stale, ref status) || status.CurrentState == ServiceStopped) break;
                Thread.Sleep(100);
            }
            if (!DeleteService(stale))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1072) AppDiagnostics.Write("Stale service deletion failed. Win32Error=" + error);
            }
            AppDiagnostics.Write("Removed stale RotaLinkInputRuntime service; the elevated session helper replaces it.");
        }
        finally { manager.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Write(ref _isRunning, 0);
        try
        {
            using var stopEvent = OpenEvent(0x0002, false, "Global\\RotaLink.SessionHelper.Stop." + _sessionId);
            if (!stopEvent.IsInvalid) SetEvent(stopEvent);

            var started = Environment.TickCount;
            while (WaitForSingleObject(_helperProcess, 100) == StoppedStatus && unchecked(Environment.TickCount - started) < 5000)
                Thread.Sleep(100);

            if (WaitForSingleObject(_helperProcess, 0) == StoppedStatus && !TerminateProcess(_helperProcess, 0x524F5441))
                AppDiagnostics.Write("TerminateProcess failed with " + Marshal.GetLastWin32Error() + ".");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Session helper stop failed.", exception);
        }
        finally { _helperProcess.Dispose(); }
        TryDelete(_directory);
        AppDiagnostics.Write("Elevated session helper stopped.");
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string RuntimeVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    private static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken for privilege adjustment failed.");
        using (token)
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue failed for " + privilegeName + ".");
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x00000002 };
            SetLastError(0);
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges failed for " + privilegeName + ".");
            var error = Marshal.GetLastWin32Error();
            if (error == 1300) throw new Win32Exception(error, "The process token does not contain " + privilegeName + ".");
        }
    }

    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : base(true) { }
        public SafeKernelHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved; public string? Desktop; public string? Title;
        public int X; public int Y; public int XSize; public int YSize; public int XCountChars; public int YCountChars;
        public int FillAttribute; public int Flags; public short ShowWindow; public short Reserved2; public IntPtr Reserved2Pointer;
        public IntPtr StandardInput; public IntPtr StandardOutput; public IntPtr StandardError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process; public IntPtr Thread; public uint ProcessId; public uint ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType; public uint CurrentState; public uint ControlsAccepted; public uint Win32ExitCode;
        public uint ServiceSpecificExitCode; public uint CheckPoint; public uint WaitHint;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeKernelHandle OpenEvent(uint desiredAccess, bool inheritHandle, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(SafeKernelHandle handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeKernelHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeKernelHandle existingToken, uint desiredAccess, IntPtr attributes, int impersonationLevel, int tokenType, out SafeKernelHandle newToken);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeKernelHandle token, bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(SafeKernelHandle token, string applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeKernelHandle token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint errorCode);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(SafeServiceHandle service, uint control, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(SafeServiceHandle service, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(SafeServiceHandle service);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CloseServiceHandle(IntPtr handle);
}
