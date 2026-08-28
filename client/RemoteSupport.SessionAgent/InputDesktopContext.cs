using System.ComponentModel;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteSupport.SessionAgent;

internal sealed class InputDesktopContext : IDisposable
{
    // GENERIC_ALL: a desktop handle switched in with a narrower access mask
    // silently breaks SendInput (ERROR_ACCESS_DENIED). The interactive
    // desktop's DACL allows GENERIC_ALL for user tokens.
    private const uint DesktopGenericAll = 0x10000000;
    private const int UoiName = 2;
    private SafeDesktopHandle? _attachedDesktop;
    private string? _desktopName;

    public string AttachToCurrentInputDesktop()
    {
        var next = OpenInputDesktop(0, false, DesktopGenericAll);
        if (next.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenInputDesktop failed.");

        var name = ReadDesktopName(next);
        if (!SetThreadDesktop(next))
        {
            var error = Marshal.GetLastWin32Error();
            next.Dispose();
            throw new Win32Exception(error, "SetThreadDesktop failed for '" + name + "'.");
        }

        var previous = _attachedDesktop;
        _attachedDesktop = next;
        _desktopName = name;
        previous?.Dispose();
        return name;
    }

    public void Dispose()
    {
        _attachedDesktop?.Dispose();
        _attachedDesktop = null;
        _desktopName = null;
    }

    private static string ReadDesktopName(SafeDesktopHandle desktop)
    {
        GetUserObjectInformation(desktop, UoiName, IntPtr.Zero, 0, out var required);
        var firstError = Marshal.GetLastWin32Error();
        if (required == 0 && firstError != 122)
            throw new Win32Exception(firstError, "GetUserObjectInformation(size) failed.");

        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetUserObjectInformation(desktop, UoiName, buffer, required, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetUserObjectInformation(name) failed.");
            return Marshal.PtrToStringUni(buffer) ?? "unknown";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed class SafeDesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDesktopHandle() : base(true) { }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern SafeDesktopHandle OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(SafeDesktopHandle desktop);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        SafeDesktopHandle handle, int index, IntPtr information, uint length, out uint needed);
}
