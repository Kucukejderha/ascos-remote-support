using System.Runtime.InteropServices;
using System.Text;

namespace RotaLink.SessionHelper;

internal sealed class ClickTargetDiagnostics
{
    private readonly HelperLog _log;
    private IntPtr _lastTarget;

    public ClickTargetDiagnostics(HelperLog log) => _log = log;

    public void Observe(AbsolutePoint point)
    {
        var hit = WindowFromPoint(new Point(point.PixelX, point.PixelY));
        if (hit == IntPtr.Zero || hit == _lastTarget) return;
        _lastTarget = hit;
        var className = new StringBuilder(256);
        var length = GetClassName(hit, className, className.Capacity);
        GetWindowThreadProcessId(hit, out var processId);
        _log.Write("Natural click target observed. Window=0x" + hit.ToInt64().ToString("X") +
            ", Class=" + (length > 0 ? className.ToString() : "unknown") +
            ", ProcessId=" + processId + ". Foreground manipulation is intentionally disabled.");
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
}
