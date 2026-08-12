using System.Runtime.InteropServices;
using System.Text;

namespace RotaLink.SessionHelper;

internal sealed class ClickTargetDiagnostics
{
    private readonly HelperLog _log;
    private IntPtr _lastTarget;
    private ClickTarget _lastShellClick;
    private int _lastShellClickTicks;

    public ClickTargetDiagnostics(HelperLog log) => _log = log;

    public ClickTarget Observe(AbsolutePoint point)
    {
        var hit = WindowFromPoint(new Point(point.PixelX, point.PixelY));
        if (hit == IntPtr.Zero) return default;
        var className = new StringBuilder(256);
        var length = GetClassName(hit, className, className.Capacity);
        var resolvedClassName = length > 0 ? className.ToString() : "unknown";
        GetWindowThreadProcessId(hit, out var processId);
        if (hit != _lastTarget)
        {
            _lastTarget = hit;
            _log.Write("Natural click target observed. Window=0x" + hit.ToInt64().ToString("X") +
                ", Class=" + resolvedClassName + ", ProcessId=" + processId +
                ". Foreground manipulation is intentionally disabled.");
        }
        return new ClickTarget(hit, resolvedClassName, point.PixelX, point.PixelY);
    }

    public bool TryDispatchExplorerShellClick(ClickTarget target, int button)
    {
        if (!target.IsExplorerShellControl || target.Window == IntPtr.Zero) return false;
        var clientPoint = new Point(target.ScreenX, target.ScreenY);
        if (!ScreenToClient(target.Window, ref clientPoint)) return false;

        var messages = button switch
        {
            0 => (Down: 0x0201u, Up: 0x0202u, Double: 0x0203u, State: 0x0001u),
            1 => (Down: 0x0207u, Up: 0x0208u, Double: 0x0209u, State: 0x0010u),
            2 => (Down: 0x0204u, Up: 0x0205u, Double: 0x0206u, State: 0x0002u),
            _ => default
        };
        if (messages.Down == 0) return false;

        var now = Environment.TickCount;
        var isDesktopDoubleClick = target.ClassName == "SysListView32" &&
            target.Window == _lastShellClick.Window && button == _lastShellClick.Button &&
            Math.Abs(target.ScreenX - _lastShellClick.ScreenX) <= 4 &&
            Math.Abs(target.ScreenY - _lastShellClick.ScreenY) <= 4 &&
            unchecked(now - _lastShellClickTicks) >= 0 &&
            unchecked(now - _lastShellClickTicks) <= GetDoubleClickTime();
        _lastShellClick = new ClickTarget(target.Window, target.ClassName, target.ScreenX, target.ScreenY, button);
        _lastShellClickTicks = now;

        var parameter = new IntPtr(unchecked((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF)));
        if (!Send(target.Window, 0x0200, 0, parameter)) return false; // WM_MOUSEMOVE
        var downMessage = isDesktopDoubleClick ? messages.Double : messages.Down;
        if (!Send(target.Window, downMessage, messages.State, parameter)) return false;
        if (!Send(target.Window, messages.Up, 0, parameter)) return false;
        _log.Write("Explorer shell click delivered synchronously. Class=" + target.ClassName +
            ", Button=" + button + ", DoubleClick=" + isDesktopDoubleClick + ".");
        return true;
    }

    private static bool Send(IntPtr window, uint message, uint state, IntPtr parameter)
    {
        const uint smtoBlock = 0x0001;
        const uint smtoAbortIfHung = 0x0002;
        return SendMessageTimeout(window, message, new IntPtr(state), parameter,
            smtoBlock | smtoAbortIfHung, 250, out _) != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct Point
    {
        public Point(int x, int y) { X = x; Y = y; }
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}

internal readonly struct ClickTarget
{
    public ClickTarget(IntPtr window, string? className, int screenX, int screenY, int button = -1)
    {
        Window = window;
        ClassName = className;
        ScreenX = screenX;
        ScreenY = screenY;
        Button = button;
    }

    public IntPtr Window { get; }
    public string? ClassName { get; }
    public int ScreenX { get; }
    public int ScreenY { get; }
    public int Button { get; }
    public bool IsExplorerShellControl => ClassName is "MSTaskListWClass" or "SysListView32" or "Shell_TrayWnd";
}
