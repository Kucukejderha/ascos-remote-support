using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RotaLink.SessionHelper;

internal sealed class InputDesktop : IDisposable
{
    // SendInput is evaluated against the desktop handle assigned to this
    // thread. Request the complete desktop access mask, including
    // DESKTOP_JOURNALPLAYBACK (0x20); a read/write-only handle can be opened
    // successfully yet input injection is then rejected with ERROR_ACCESS_DENIED.
    private const uint DesktopAllAccess = 0x000F01FF;
    private SafeDesktopHandle? _current;

    public void Refresh()
    {
        var next = OpenInputDesktop(0, false, DesktopAllAccess);
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
