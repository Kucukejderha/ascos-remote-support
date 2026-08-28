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
        if (!SetThreadDesktop(next))
        {
            var error = Marshal.GetLastWin32Error();
            next.Dispose();
            throw new Win32Exception(error, "SetThreadDesktop failed.");
        }
        var previous = Interlocked.Exchange(ref _current, next);
        previous?.Dispose();
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
}
