using System.Net;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        EnablePhysicalPixelCoordinates();
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        string identityName;
        using (var currentIdentity = WindowsIdentity.GetCurrent()) identityName = currentIdentity.Name;
        AppDiagnostics.Write("RotaLink v" + version + " started in the interactive user session on " + Environment.OSVersion +
            ". Session=" + Process.GetCurrentProcess().SessionId + ", Identity=" + identityName +
            ", Elevated=" + IsProcessElevated() + ", Bitness=" + (Environment.Is64BitProcess ? "x64" : "x86") + ".");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }

    private static void EnablePhysicalPixelCoordinates()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
        }
        catch (EntryPointNotFoundException) { }

        SetProcessDPIAware();
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var size = Marshal.SizeOf(typeof(int));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            return GetTokenInformation(identity.Token, 20, buffer, size, out _) && Marshal.ReadInt32(buffer) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int informationLength, out int returnLength);
}
