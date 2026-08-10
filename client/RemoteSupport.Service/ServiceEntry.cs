using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RemoteSupport.Service;

internal static class ServiceEntry
{
    private const string ServiceName = "RotaLinkRemoteSupport";
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceStartPending = 2;
    private const uint ServiceStopPending = 3;
    private const uint ServiceRunning = 4;
    private const uint ServiceStopped = 1;
    private const uint ServiceAcceptStop = 0x1;
    private const uint ServiceControlStop = 0x1;
    private static readonly ServiceMainCallback ServiceMainDelegate = ServiceMain;
    private static readonly HandlerCallback HandlerDelegate = Handler;
    private static readonly ManualResetEventSlim StopRequested = new(false);
    private static IntPtr _statusHandle;
    private static ServiceStatus _status;

    public static int Run(string[] args)
    {
        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
            return RunConsole();

        var table = new[]
        {
            new ServiceTableEntry { Name = ServiceName, Callback = ServiceMainDelegate },
            new ServiceTableEntry()
        };
        if (StartServiceCtrlDispatcher(table)) return 0;
        var error = Marshal.GetLastWin32Error();
        if (error == 1063) return RunConsole(); // Direct developer execution.
        throw new Win32Exception(error, "StartServiceCtrlDispatcher failed.");
    }

    private static int RunConsole()
    {
        var logger = new ServiceLog();
        using var supervisor = new SessionHelperSupervisor(logger);
        using var window = new SessionNotificationWindow((reason, session) =>
        {
            logger.Write("Session change " + reason + ", session " + session + ".");
            _ = ReconcileAsync(supervisor, logger);
        }, logger);
        window.Start();
        supervisor.EnsureActiveSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var reconcileTimer = new Timer(_ => _ = ReconcileAsync(supervisor, logger), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; StopRequested.Set(); };
        StopRequested.Wait();
        supervisor.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }

    private static void ServiceMain(int argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, HandlerDelegate, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;
        ReportStatus(ServiceStartPending, 0, 20_000);

        var logger = new ServiceLog();
        try
        {
            using var supervisor = new SessionHelperSupervisor(logger);
            using var window = new SessionNotificationWindow((reason, session) =>
            {
                logger.Write("Session change " + reason + ", session " + session + ".");
                _ = ReconcileAsync(supervisor, logger);
            }, logger);
            window.Start();
            supervisor.EnsureActiveSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
            using var reconcileTimer = new Timer(_ => _ = ReconcileAsync(supervisor, logger), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            ReportStatus(ServiceRunning, ServiceAcceptStop, 0);
            StopRequested.Wait();
            ReportStatus(ServiceStopPending, 0, 10_000);
            supervisor.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            ReportStatus(ServiceStopped, 0, 0);
        }
        catch (Exception exception)
        {
            logger.Write("RotaLink service failed: " + exception);
            ReportStatus(ServiceStopped, 0, 0, unchecked((uint)exception.HResult));
        }
    }

    private static async Task ReconcileAsync(SessionHelperSupervisor supervisor, ServiceLog logger)
    {
        try { await supervisor.EnsureActiveSessionAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { logger.Write("Interactive helper reconciliation failed: " + exception); }
    }

    private static uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control == ServiceControlStop) StopRequested.Set();
        return 0;
    }

    private static void ReportStatus(uint currentState, uint acceptedControls, uint waitHint, uint win32ExitCode = 0)
    {
        _status.ServiceType = ServiceWin32OwnProcess;
        _status.CurrentState = currentState;
        _status.ControlsAccepted = acceptedControls;
        _status.Win32ExitCode = win32ExitCode;
        _status.WaitHint = waitHint;
        _status.CheckPoint = currentState is ServiceStartPending or ServiceStopPending ? _status.CheckPoint + 1 : 0;
        if (_statusHandle != IntPtr.Zero && !SetServiceStatus(_statusHandle, ref _status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetServiceStatus failed.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMainCallback(int argumentCount, IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint HandlerCallback(uint control, uint eventType, IntPtr eventData, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ServiceTableEntry { public string? Name; public ServiceMainCallback? Callback; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType; public uint CurrentState; public uint ControlsAccepted; public uint Win32ExitCode;
        public uint ServiceSpecificExitCode; public uint CheckPoint; public uint WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerCallback handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);
}
