using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace RemoteSupport.Service;

internal sealed class SessionHelperSupervisor : IDisposable
{
    private const uint InvalidSessionId = 0xFFFFFFFF;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenAdjustSessionId = 0x0100;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const int TokenSessionId = 12;
    private readonly ILogger<SessionHelperSupervisor> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SafeProcessHandle? _helperProcess;
    private uint _helperSessionId = InvalidSessionId;
    private bool _disposed;

    public SessionHelperSupervisor(ILogger<SessionHelperSupervisor> logger) => _logger = logger;

    public async Task EnsureActiveSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var activeSession = WTSGetActiveConsoleSessionId();
            if (activeSession == InvalidSessionId)
            {
                await StopHelperCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_helperSessionId == activeSession && _helperProcess is { IsInvalid: false, IsClosed: false } &&
                WaitForSingleObject(_helperProcess, 0) == 0x00000102) return;

            await StopHelperCoreAsync(cancellationToken).ConfigureAwait(false);
            _helperProcess = LaunchHelper(activeSession, out var processId);
            _helperSessionId = activeSession;
            _logger.LogInformation("RotaLink.SessionHelper started as SYSTEM in interactive session {SessionId}, process {ProcessId}.", activeSession, processId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopHelperCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private SafeProcessHandle LaunchHelper(uint sessionId, out uint processId)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "RotaLink.SessionHelper.exe");
        if (!File.Exists(helperPath))
            throw new FileNotFoundException("The interactive session helper is missing.", helperPath);

        EnablePrivilege("SeAssignPrimaryTokenPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");

        if (!OpenProcessToken(GetCurrentProcess(), TokenAssignPrimary | TokenDuplicate | TokenQuery |
                TokenAdjustPrivileges | TokenAdjustSessionId, out var serviceToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
        using (serviceToken)
        {
            if (!DuplicateTokenEx(serviceToken, 0x000F01FF, IntPtr.Zero, 2, 1, out var sessionToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed.");
            using (sessionToken)
            {
                var sessionBuffer = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteInt32(sessionBuffer, unchecked((int)sessionId));
                    if (!SetTokenInformation(sessionToken, TokenSessionId, sessionBuffer, sizeof(uint)))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId) failed.");
                }
                finally { Marshal.FreeHGlobal(sessionBuffer); }

                var environment = IntPtr.Zero;
                try
                {
                    if (!CreateEnvironmentBlock(out environment, sessionToken, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEnvironmentBlock failed.");

                    var startup = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfo>(),
                        Desktop = "winsta0\\default"
                    };
                    var commandLine = "\"" + helperPath + "\" --service-child --session " + sessionId;
                    if (!CreateProcessAsUser(sessionToken, helperPath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                            CreateUnicodeEnvironment | CreateNoWindow, environment, AppContext.BaseDirectory,
                            ref startup, out var processInformation))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed.");

                    using var thread = new SafeKernelHandle(processInformation.Thread);
                    processId = processInformation.ProcessId;
                    return new SafeProcessHandle(processInformation.Process, true);
                }
                finally
                {
                    if (environment != IntPtr.Zero && !DestroyEnvironmentBlock(environment))
                        _logger.LogWarning("DestroyEnvironmentBlock failed with {Win32Error}.", Marshal.GetLastWin32Error());
                }
            }
        }
    }

    private async Task StopHelperCoreAsync(CancellationToken cancellationToken)
    {
        var process = _helperProcess;
        var sessionId = _helperSessionId;
        _helperProcess = null;
        _helperSessionId = InvalidSessionId;
        if (process is null) return;

        try
        {
            using var stopEvent = OpenEvent(0x0002, false, "Global\\RotaLink.SessionHelper.Stop." + sessionId);
            if (!stopEvent.IsInvalid) SetEvent(stopEvent);

            var started = Environment.TickCount;
            while (WaitForSingleObject(process, 100) == 0x00000102 && unchecked(Environment.TickCount - started) < 5000)
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            if (WaitForSingleObject(process, 0) == 0x00000102)
            {
                _logger.LogWarning("Session helper did not stop gracefully; terminating its owned process.");
                if (!TerminateProcess(process, 0x524F5441))
                    _logger.LogWarning("TerminateProcess failed with {Win32Error}.", Marshal.GetLastWin32Error());
            }
        }
        finally { process.Dispose(); }
    }

    private static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken for privilege adjustment failed.");
        using (token)
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue failed for " + privilegeName + ".");
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x00000002 };
            Marshal.SetLastPInvokeError(0);
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges failed for " + privilegeName + ".");
            var error = Marshal.GetLastWin32Error();
            if (error == 1300) throw new Win32Exception(error, "The service token does not contain " + privilegeName + ".");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _helperProcess?.Dispose();
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionHelperSupervisor));
    }

    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : base(true) { }
        public SafeKernelHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
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

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeKernelHandle OpenEvent(uint desiredAccess, bool inheritHandle, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(SafeKernelHandle handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeKernelHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeKernelHandle existingToken, uint desiredAccess, IntPtr attributes, int impersonationLevel, int tokenType, out SafeKernelHandle newToken);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(SafeKernelHandle token, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeKernelHandle token, bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(SafeKernelHandle token, string applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeKernelHandle token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
}
