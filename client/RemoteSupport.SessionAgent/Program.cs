using System.Net;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        if (!IsAdministrator())
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = string.Join(" ", args.Select(value => "\"" + value.Replace("\"", "\\\"") + "\"")),
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                MessageBox.Show("Uzaktan fare ve klavye kontrolü için Windows yönetici izni gereklidir.", "RotaLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        EnablePhysicalPixelCoordinates();
        AppDiagnostics.Write("RotaLink v0.6.0 started elevated on " + Environment.OSVersion);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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
