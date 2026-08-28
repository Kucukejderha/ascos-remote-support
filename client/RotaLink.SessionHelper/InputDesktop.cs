using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RotaLink.SessionHelper;

internal sealed class InputDesktop : IDisposable
{
    // GENERIC_ALL: a desktop handle switched in with a narrower access mask
    // silently breaks SendInput (ERROR_ACCESS_DENIED). The interactive
    // desktop's DACL allows GENERIC_ALL for user tokens.
    private const uint RequiredAccess = 0x10000000;
    private SafeDesktopHandle? _current;

    public void Refresh()
    {
        var next = OpenInputDesktop(0, false, RequiredAccess);
        if (next.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenInputDesktop failed.");

        // Switching desktops resets the thread DPI context and, on Windows 11,
        // the context cannot be restored afterwards (SetThreadDpiAwarenessContext
        // fails). SendInput absolute coordinates then map against the logical
        // resolution and every click misses small targets like title-bar
        // buttons. Only switch when the desktop really changed; the thread is
        // born on the interactive desktop and stays DPI-aware that way.
        var current = GetThreadDesktop(GetCurrentThreadId());
        if (DesktopNameEquals(current, next))
        {
            next.Dispose();
            return;
        }

        if (!SetThreadDesktop(next))
        {
            var error = Marshal.GetLastWin32Error();
            next.Dispose();
            throw new Win32Exception(error, "SetThreadDesktop failed.");
        }
        var previous = Interlocked.Exchange(ref _current, next);
        previous?.Dispose();
    }

    private static bool DesktopNameEquals(IntPtr first, SafeDesktopHandle second)
    {
        if (first == IntPtr.Zero) return false;
        return string.Equals(ReadDesktopName(first), ReadDesktopName(second.DangerousGetHandle()), StringComparison.Ordinal);
    }

    private static string ReadDesktopName(IntPtr handle)
    {
        var required = 0u;
        GetUserObjectInformation(handle, 2, IntPtr.Zero, 0, out required);
        if (required == 0) return "";
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!GetUserObjectInformation(handle, 2, buffer, required, out required)) return "";
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _current, null)?.Dispose();

    private sealed class SafeDesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDesktopHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern SafeDesktopHandle OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(SafeDesktopHandle desktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, IntPtr info, uint length, out uint needed);
}
