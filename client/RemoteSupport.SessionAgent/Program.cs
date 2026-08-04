using System.Net;
using System.Windows.Forms;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        AppDiagnostics.Write("RotaLink v0.4.2 started on " + Environment.OSVersion);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }
}
