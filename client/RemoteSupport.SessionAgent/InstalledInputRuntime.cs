using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteSupport.SessionAgent;

internal sealed class InstalledInputRuntime : IDisposable
{
    private const string ServiceName = "RotaLinkInputRuntime";
    private const string RuntimeVersion = "1.1.0-alpha.25";
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceDemandStart = 0x00000003;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private static int _isRunning;
    private readonly SafeServiceHandle _manager;
    private readonly SafeServiceHandle _service;
    private bool _disposed;

    public static bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    private InstalledInputRuntime(SafeServiceHandle manager, SafeServiceHandle service)
    {
        _manager = manager;
        _service = service;
    }

    public static InstalledInputRuntime? TryStart()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            EnsureRuntimeResources(assembly);
#if !UNSIGNED_DEVELOPMENT
            AuthenticodeTrust.VerifyTrusted(Application.ExecutablePath);
#endif
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
                throw new InvalidOperationException("Windows Program Files directory could not be resolved.");

            var directory = Path.Combine(programFiles, "RotaLink", "Runtime", RuntimeVersion);
            Directory.CreateDirectory(directory);
            var servicePath = Path.Combine(directory, "RotaLink.Service.exe");
            var helperPath = Path.Combine(directory, "RotaLink.SessionHelper.exe");

            var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
            if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.");
            SafeServiceHandle? service = null;
            try
            {
                service = OpenService(manager, ServiceName,
                    ServiceChangeConfig | ServiceQueryStatus | ServiceStart | ServiceStop);
                if (!service.IsInvalid) StopServiceIfRunning(service);
                else if (Marshal.GetLastWin32Error() != ErrorServiceDoesNotExist)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenService failed.");

                ExtractTrusted(assembly, "RotaLink.Runtime.Service.exe", servicePath);
                ExtractTrusted(assembly, "RotaLink.Runtime.SessionHelper.exe", helperPath);

                var quotedPath = "\"" + servicePath + "\"";
                if (service.IsInvalid)
                {
                    service.Dispose();
                    service = CreateService(manager, ServiceName, "RotaLink Interactive Input Runtime",
                        ServiceChangeConfig | ServiceQueryStatus | ServiceStart | ServiceStop,
                        ServiceWin32OwnProcess, ServiceDemandStart, ServiceErrorNormal, quotedPath,
                        null, IntPtr.Zero, null, null, null);
                    if (service.IsInvalid)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateService failed.");
                }
                else if (!ChangeServiceConfig(service, ServiceWin32OwnProcess, ServiceDemandStart,
                             ServiceErrorNormal, quotedPath, null, IntPtr.Zero, null, null, null, null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig failed.");
                }

                using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var serviceArguments = new[] { "--client-pid", currentProcess.Id.ToString() };
                if (!StartService(service, serviceArguments.Length, serviceArguments))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorServiceAlreadyRunning)
                    {
                        service.Dispose();
                        throw new Win32Exception(error, "StartService failed.");
                    }
                }

                WaitUntilRunning(service);
                Volatile.Write(ref _isRunning, 1);
#if UNSIGNED_DEVELOPMENT
                AppDiagnostics.Write("UNSIGNED DEVELOPMENT interactive-token input runtime is running from Program Files; do not distribute this test build.");
#else
                AppDiagnostics.Write("Signed interactive-token input runtime is running from Program Files; SessionHelper IPC will become available shortly.");
#endif
                return new InstalledInputRuntime(manager, service);
            }
            catch
            {
                service?.Dispose();
                manager.Dispose();
                throw;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("SYSTEM input runtime could not be started.", exception);
            return null;
        }
    }

    private static void EnsureRuntimeResources(Assembly assembly)
    {
        if (assembly.GetManifestResourceInfo("RotaLink.Runtime.Service.exe") is null ||
            assembly.GetManifestResourceInfo("RotaLink.Runtime.SessionHelper.exe") is null)
            throw new InvalidOperationException("Signed input runtime is not embedded in this RotaLink build.");
    }

    private static void ExtractTrusted(Assembly assembly, string resourceName, string destination)
    {
        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing runtime resource " + resourceName + ".");
        var temporary = destination + ".new";
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(output);
                output.Flush(true);
            }
#if !UNSIGNED_DEVELOPMENT
            AuthenticodeTrust.VerifyTrusted(temporary);
#endif
            if (File.Exists(destination)) File.Replace(temporary, destination, null, true);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void StopServiceIfRunning(SafeServiceHandle service)
    {
        var status = new ServiceStatus();
        if (!QueryServiceStatus(service, ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatus failed.");
        if (status.CurrentState == ServiceStopped) return;
        if (!ControlService(service, ServiceControlStop, ref status))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1062) throw new Win32Exception(error, "ControlService(STOP) failed.");
        }
        var deadline = Environment.TickCount + 10_000;
        do
        {
            Thread.Sleep(100);
            if (!QueryServiceStatus(service, ref status))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatus failed while stopping.");
            if (status.CurrentState == ServiceStopped) return;
        } while (unchecked(Environment.TickCount - deadline) < 0);
        throw new TimeoutException("RotaLink input service did not stop within 10 seconds.");
    }

    private static void WaitUntilRunning(SafeServiceHandle service)
    {
        var deadline = Environment.TickCount + 10_000;
        var status = new ServiceStatus();
        while (true)
        {
            if (!QueryServiceStatus(service, ref status))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatus failed while starting.");
            if (status.CurrentState == ServiceRunning) return;
            if (status.CurrentState == ServiceStopped)
                throw new Win32Exception(unchecked((int)status.Win32ExitCode),
                    "Input runtime stopped during startup. ServiceSpecificExitCode=" + status.ServiceSpecificExitCode + ".");
            if (status.CurrentState != ServiceStartPending)
                throw new InvalidOperationException("Input runtime entered unexpected state " + status.CurrentState + ".");
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
        try { StopServiceIfRunning(_service); }
        catch (Exception exception) { AppDiagnostics.Write("Installed input runtime could not be stopped cleanly.", exception); }
        _service.Dispose();
        _manager.Dispose();
        AppDiagnostics.Write("Installed SYSTEM input runtime stopped; signed files and service registration were retained.");
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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle CreateService(SafeServiceHandle manager, string serviceName,
        string displayName, uint desiredAccess, uint serviceType, uint startType, uint errorControl,
        string binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(SafeServiceHandle service, uint serviceType, uint startType,
        uint errorControl, string binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies,
        string? account, string? password, string? displayName);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(SafeServiceHandle service, int argumentCount, string[]? arguments);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(SafeServiceHandle service, uint control, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(SafeServiceHandle service, ref ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}

internal static class AuthenticodeTrust
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static void VerifyTrusted(string path)
    {
        var file = new WinTrustFileInfo(path);
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData(filePointer);
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (result != 0)
                throw new InvalidDataException("Authenticode trust verification failed for '" + path + "'. HRESULT=0x" +
                    result.ToString("X8") + ".");
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
        public WinTrustFileInfo(string path)
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(); FilePath = path;
            FileHandle = IntPtr.Zero; KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size; public IntPtr PolicyCallbackData; public IntPtr SipClientData;
        public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo;
        public uint StateAction; public IntPtr StateData; public string? UrlReference; public uint ProviderFlags;
        public uint UiContext;
        public WinTrustData(IntPtr fileInfo)
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(); PolicyCallbackData = IntPtr.Zero; SipClientData = IntPtr.Zero;
            UiChoice = 2; RevocationChecks = 0; UnionChoice = 1; FileInfo = fileInfo; StateAction = 0;
            StateData = IntPtr.Zero; UrlReference = null; ProviderFlags = 0; UiContext = 0;
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint WinVerifyTrust(IntPtr window, [In] ref Guid actionId, ref WinTrustData trustData);
}
