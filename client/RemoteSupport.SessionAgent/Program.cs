using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        EnablePhysicalPixelCoordinates();
        AppDiagnostics.Write("RotaLink v0.8.0 started in the interactive user session on " + Environment.OSVersion);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();
}
