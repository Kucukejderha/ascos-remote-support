using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace RotaLink.SessionHelper;

/// <summary>
/// Resolves the native window below a remote click and uses Windows UI Automation
/// only for shell controls whose behaviour cannot be reproduced with window messages.
/// All other clicks remain real, atomic SendInput sequences in InputEngine.
/// </summary>
internal sealed class ClickTargetDiagnostics
{
    private readonly HelperLog _log;
    private IntPtr _lastTarget;
    private string? _lastDesktopElement;
    private int _lastDesktopClickTicks;

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
                ", Class=" + resolvedClassName + ", ProcessId=" + processId + ".");
        }
        return new ClickTarget(hit, resolvedClassName, point.PixelX, point.PixelY);
    }

    public ShellClickResult HandleExplorerShellLeftClick(ClickTarget target)
    {
        if (!target.IsExplorerShellControl || target.Window == IntPtr.Zero) return ShellClickResult.FallBackToSendInput;

        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(target.ScreenX, target.ScreenY));
            if (element is null) return ShellClickResult.FallBackToSendInput;

            if (target.ClassName == "SysListView32")
                return TrySelectOrOpenDesktopItem(element);

            return TryInvokeTaskbarElement(element)
                ? ShellClickResult.Handled
                : ShellClickResult.FallBackToSendInput;
        }
        catch (ElementNotAvailableException exception)
        {
            _log.Write("Shell automation target disappeared; falling back to SendInput. " + exception.Message);
            return ShellClickResult.FallBackToSendInput;
        }
        catch (InvalidOperationException exception)
        {
            _log.Write("Shell automation was unavailable; falling back to SendInput. " + exception.Message);
            return ShellClickResult.FallBackToSendInput;
        }
        catch (COMException exception)
        {
            _log.Write("Shell automation COM call failed; falling back to SendInput. HRESULT=0x" +
                exception.HResult.ToString("X8") + ".");
            return ShellClickResult.FallBackToSendInput;
        }
    }

    private ShellClickResult TrySelectOrOpenDesktopItem(AutomationElement element)
    {
        // Empty desktop space resolves to the list itself and has no SelectionItem
        // pattern. Returning false deliberately sends a real click, which clears an
        // icon selection and dismisses an open context menu exactly like local input.
        if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject))
            return ShellClickResult.FallBackToSendInput;

        var identity = GetElementIdentity(element);
        var now = Environment.TickCount;
        var elapsed = unchecked(now - _lastDesktopClickTicks);
        var isDoubleClick = identity == _lastDesktopElement && elapsed >= 0 && elapsed <= GetDoubleClickTime();
        _lastDesktopElement = identity;
        _lastDesktopClickTicks = now;

        if (isDoubleClick && TryInvokeElement(element, allowParentSearch: false))
        {
            _lastDesktopElement = null;
            _log.Write("Desktop item opened through UI Automation. Name=" + SafeName(element) + ".");
            return ShellClickResult.Handled;
        }

        if (isDoubleClick)
        {
            _lastDesktopElement = null;
            _log.Write("Desktop item has no automation invoke pattern; using a real double-click. Name=" + SafeName(element) + ".");
            return ShellClickResult.PhysicalDoubleClick;
        }

        ((SelectionItemPattern)selectionObject).Select();
        element.SetFocus();
        _log.Write("Desktop item selected through UI Automation. Name=" + SafeName(element) + ".");
        return ShellClickResult.Handled;
    }

    private bool TryInvokeTaskbarElement(AutomationElement element)
    {
        if (!TryInvokeElement(element, allowParentSearch: true)) return false;
        _log.Write("Taskbar control invoked through UI Automation. Name=" + SafeName(element) + ".");
        return true;
    }

    private static bool TryInvokeElement(AutomationElement start, bool allowParentSearch)
    {
        var current = start;
        for (var depth = 0; current is not null && depth < (allowParentSearch ? 6 : 1); depth++)
        {
            if (current.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject))
            {
                ((InvokePattern)invokeObject).Invoke();
                return true;
            }

            current = allowParentSearch ? TreeWalker.ControlViewWalker.GetParent(current) : null;
        }
        return false;
    }

    private static string GetElementIdentity(AutomationElement element)
    {
        var runtimeId = element.GetRuntimeId();
        return runtimeId is { Length: > 0 }
            ? string.Join(".", runtimeId)
            : element.Current.NativeWindowHandle + ":" + element.Current.AutomationId + ":" + SafeName(element);
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name ?? string.Empty; }
        catch (ElementNotAvailableException) { return "<unavailable>"; }
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
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
}

internal readonly struct ClickTarget
{
    public ClickTarget(IntPtr window, string? className, int screenX, int screenY)
    {
        Window = window;
        ClassName = className;
        ScreenX = screenX;
        ScreenY = screenY;
    }

    public IntPtr Window { get; }
    public string? ClassName { get; }
    public int ScreenX { get; }
    public int ScreenY { get; }
    public bool IsExplorerShellControl => ClassName is "MSTaskListWClass" or "SysListView32" or "Shell_TrayWnd";
}

internal enum ShellClickResult
{
    FallBackToSendInput,
    Handled,
    PhysicalDoubleClick
}
