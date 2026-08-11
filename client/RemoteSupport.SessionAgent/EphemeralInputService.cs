using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteSupport.SessionAgent;

internal sealed class EphemeralInputService : IDisposable
{
    private static int _isRunning;
    private const string ServiceName = "RotaLinkInputRuntime";
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint Delete = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceDemandStart = 0x00000003;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceRunning = 0x00000004;
    private readonly SafeServiceHandle _manager;
    private readonly SafeServiceHandle _service;
    private readonly string _directory;
    private bool _disposed;

    public static bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    private EphemeralInputService(SafeServiceHandle manager, SafeServiceHandle service, string directory)
    {
        _manager = manager;
        _service = service;
        _directory = directory;
    }

    public static EphemeralInputService? TryStart()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            if (assembly.GetManifestResourceInfo("RotaLink.Runtime.Service.exe") is null ||
                assembly.GetManifestResourceInfo("RotaLink.Runtime.SessionHelper.exe") is null)
            {
                AppDiagnostics.Write("Privileged input runtime is not embedded; direct interactive input mode remains active.");
                return null;
            }

            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RotaLink", "SessionRuntime", "1.1.0-alpha.14");
            Directory.CreateDirectory(directory);
            var servicePath = Path.Combine(directory, "RotaLink.Service.exe");
            var helperPath = Path.Combine(directory, "RotaLink.SessionHelper.exe");

            var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
            if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.");
            try
            {
                RemoveStaleService(manager);
                Extract(assembly, "RotaLink.Runtime.Service.exe", servicePath);
                Extract(assembly, "RotaLink.Runtime.SessionHelper.exe", helperPath);
                var quotedPath = "\"" + servicePath + "\"";
                var service = CreateService(manager, ServiceName, "RotaLink Interactive Input Runtime",
                    ServiceQueryStatus | ServiceStart | ServiceStop | Delete, ServiceWin32OwnProcess,
                    ServiceDemandStart, ServiceErrorNormal, quotedPath, null, IntPtr.Zero, null, null, null);
                if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateService failed.");
                if (!StartService(service, 0, null))
                {
                    var error = Marshal.GetLastWin32Error();
                    service.Dispose();
                    throw new Win32Exception(error, "StartService failed.");
                }
                WaitUntilRunning(service);
                Volatile.Write(ref _isRunning, 1);
                AppDiagnostics.Write("Temporary SYSTEM input service is RUNNING; SessionHelper IPC will become available shortly.");
                return new EphemeralInputService(manager, service, directory);
            }
            catch
            {
                manager.Dispose();
                throw;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Privileged input runtime could not be started; control will fail honestly if SendInput is rejected.", exception);
            return null;
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

    private static void RemoveStaleService(SafeServiceHandle manager)
    {
        using var stale = OpenService(manager, ServiceName, ServiceQueryStatus | ServiceStop | Delete);
        if (stale.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1060) throw new Win32Exception(error, "OpenService failed.");
            return;
        }

        var status = new ServiceStatus();
        ControlService(stale, ServiceControlStop, ref status);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!QueryServiceStatus(stale, ref status) || status.CurrentState == 1) break;
            Thread.Sleep(100);
        }
        if (!DeleteService(stale))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1072) throw new Win32Exception(error, "DeleteService failed.");
        }
    }

    private static void WaitUntilRunning(SafeServiceHandle service)
    {
        var deadline = Environment.TickCount + 10000;
        var status = new ServiceStatus();
        while (true)
        {
            if (!QueryServiceStatus(service, ref status))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatus failed while starting input runtime.");
            if (status.CurrentState == ServiceRunning) return;
            if (status.CurrentState == ServiceStopped)
                throw new Win32Exception(unchecked((int)status.Win32ExitCode),
                    "Input runtime stopped during startup. ServiceSpecificExitCode=" + status.ServiceSpecificExitCode + ".");
            if (status.CurrentState != ServiceStartPending)
                throw new InvalidOperationException("Input runtime entered unexpected service state " + status.CurrentState + ".");
            if (unchecked(Environment.TickCount - deadline) >= 0)
                throw new TimeoutException("Input runtime did not reach RUNNING state within 10 seconds.");
            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Write(ref _isRunning, 0);
        var status = new ServiceStatus();
        ControlService(_service, ServiceControlStop, ref status);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!QueryServiceStatus(_service, ref status) || status.CurrentState == 1) break;
            Thread.Sleep(100);
        }
        if (!DeleteService(_service))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1072) AppDiagnostics.Write("Temporary input service deletion failed. Win32Error=" + error);
        }
        _service.Dispose();
        _manager.Dispose();
        TryDelete(_directory);
        AppDiagnostics.Write("Temporary SYSTEM input service stopped.");
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType; public uint CurrentState; public uint ControlsAccepted; public uint Win32ExitCode;
        public uint ServiceSpecificExitCode; public uint CheckPoint; public uint WaitHint;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeServiceHandle CreateService(SafeServiceHandle manager, string serviceName, string displayName, uint desiredAccess, uint serviceType, uint startType, uint errorControl, string binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(SafeServiceHandle service, int argumentCount, string[]? arguments);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(SafeServiceHandle service, uint control, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatus(SafeServiceHandle service, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteService(SafeServiceHandle service);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
}
