using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RotaLink.SessionHelper;

internal sealed class ForegroundActivation
{
    private const uint GaRoot = 2;
    private readonly HelperLog _log;
    private IntPtr _lastActivatedWindow;

    public ForegroundActivation(HelperLog log) => _log = log;

    public void PrepareForClick(AbsolutePoint point)
    {
        var hit = WindowFromPoint(new Point(point.PixelX, point.PixelY));
        if (hit == IntPtr.Zero) return;
        var root = GetAncestor(hit, GaRoot);
        if (root == IntPtr.Zero) root = hit;
        if (!IsWindow(root) || !IsWindowVisible(root)) return;

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(root, out _);
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0u : GetWindowThreadProcessId(foreground, out _);
        var attachedTarget = false;
        var attachedForeground = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                if (!AttachThreadInput(currentThread, targetThread, true))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "AttachThreadInput(target) failed.");
                attachedTarget = true;
            }
            if (foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread)
            {
                if (!AttachThreadInput(currentThread, foregroundThread, true))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "AttachThreadInput(foreground) failed.");
                attachedForeground = true;
            }

            // A local click activates the root window before delivering the button
            // message. Reproduce that foreground transition without generating a
            // second click or relying on another remote-control product to wake it.
            var foregrounded = SetForegroundWindow(root);
            var activated = SetActiveWindow(root) != IntPtr.Zero;
            var focused = SetFocus(hit) != IntPtr.Zero;
            if (_lastActivatedWindow != root)
            {
                _lastActivatedWindow = root;
                _log.Write("Prepared foreground input target. Window=0x" + root.ToInt64().ToString("X") +
                    ", Hit=0x" + hit.ToInt64().ToString("X") + ", Foreground=" + foregrounded +
                    ", Active=" + activated + ", Focus=" + focused + ".");
            }
        }
        catch (Win32Exception exception)
        {
            _log.Write("Foreground input preparation failed; SendInput will still be attempted. Win32Error=" +
                exception.NativeErrorCode + ". " + exception.Message);
        }
        finally
        {
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct Point
    {
        public Point(int x, int y) { X = x; Y = y; }
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
}
