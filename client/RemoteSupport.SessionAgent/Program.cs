using System.Net;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
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

        AppDiagnostics.Write("RotaLink v0.5.0 started elevated on " + Environment.OSVersion);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
