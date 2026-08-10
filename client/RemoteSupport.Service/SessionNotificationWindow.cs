using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace RemoteSupport.Service;

internal enum SessionChangeReason : uint
{
    ConsoleConnect = 0x1,
    ConsoleDisconnect = 0x2,
    RemoteConnect = 0x3,
    RemoteDisconnect = 0x4,
    SessionLogon = 0x5,
    SessionLogoff = 0x6,
    SessionLock = 0x7,
    SessionUnlock = 0x8,
    SessionRemoteControl = 0x9
}

internal sealed class SessionNotificationWindow : IDisposable
{
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private static readonly IntPtr HwndMessage = new(-3);
    private readonly Action<SessionChangeReason, uint> _callback;
    private readonly ILogger _logger;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly WndProc _windowProcedure;
    private Thread? _thread;
    private IntPtr _window;
    private Exception? _startupError;

    public SessionNotificationWindow(Action<SessionChangeReason, uint> callback, ILogger logger)
    {
        _callback = callback;
        _logger = logger;
        _windowProcedure = WindowProcedure;
    }

    public void Start()
    {
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "RotaLink WTS notifications" };
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The WTS notification window did not start.");
        if (_startupError is not null)
            throw new InvalidOperationException("The WTS notification window failed to start.", _startupError);
    }

    private void MessageLoop()
    {
        var className = "RotaLink.WtsNotification." + Environment.ProcessId;
        try
        {
            var instance = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Instance = instance,
                ClassName = className,
                WindowProcedure = _windowProcedure
            };
            if (RegisterClassEx(ref windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");

            _window = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
                HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
            if (_window == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
            if (!WTSRegisterSessionNotification(_window, 1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSRegisterSessionNotification failed.");

            _started.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupError = exception;
            _started.Set();
        }
        finally
        {
            if (_window != IntPtr.Zero)
            {
                WTSUnRegisterSessionNotification(_window);
                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }
            UnregisterClass(className, GetModuleHandle(null));
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmWtsSessionChange)
        {
            try { _callback((SessionChangeReason)(uint)wParam.ToInt64(), unchecked((uint)lParam.ToInt64())); }
            catch (Exception exception) { _logger.LogError(exception, "WTS session callback failed."); }
            return IntPtr.Zero;
        }
        if (message == WmClose)
        {
            WTSUnRegisterSessionNotification(window);
            DestroyWindow(window);
            return IntPtr.Zero;
        }
        if (message == WmDestroy)
        {
            _window = IntPtr.Zero;
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero) PostMessage(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
        if (_thread is { IsAlive: true } && !_thread.Join(TimeSpan.FromSeconds(5)))
            _logger.LogWarning("WTS notification thread did not stop within five seconds.");
        _started.Dispose();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WndProc? WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}
