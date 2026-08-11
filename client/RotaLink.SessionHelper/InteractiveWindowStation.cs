using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RotaLink.SessionHelper;

internal sealed class InteractiveWindowStation : IDisposable
{
    private const uint WindowStationAllAccess = 0x0000037F;
    private readonly SafeWindowStationHandle _handle;

    private InteractiveWindowStation(SafeWindowStationHandle handle) => _handle = handle;

    public static InteractiveWindowStation Attach()
    {
        var handle = OpenWindowStation("WinSta0", false, WindowStationAllAccess);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenWindowStation(WinSta0) failed.");

        if (!SetProcessWindowStation(handle))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "SetProcessWindowStation(WinSta0) failed.");
        }

        return new InteractiveWindowStation(handle);
    }

    public void Dispose() => _handle.Dispose();

    private sealed class SafeWindowStationHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeWindowStationHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseWindowStation(handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWindowStationHandle OpenWindowStation(string name, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWindowStation(SafeWindowStationHandle windowStation);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseWindowStation(IntPtr windowStation);
}
