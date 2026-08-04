using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Net;

namespace RemoteSupport.SessionAgent;

internal static class Program
{
    private const uint YesNoInformation = 0x00000004u | 0x00000040u | 0x00010000u;
    private const uint OkCancelInformation = 0x00000001u | 0x00000040u | 0x00010000u;

    [STAThread]
    private static async Task Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var server = new Uri(args.FirstOrDefault() ?? "https://45.87.173.201.nip.io");
        try
        {
            using var identity = new ECDsaCng(ECCurve.NamedCurves.nistP256);
            using var api = new SignalingHostClient(server, identity);
            var session = await api.CreateSessionAsync(Environment.MachineName, CancellationToken.None);
            var approved = MessageBox(IntPtr.Zero,
                $"Rotaniz Remote Support\n\nDestek kodunuz:  {session.Code}\n\nBu kodu destek personeline iletin. Ekran paylaşımı ve uzaktan fare/klavye kontrolünü başlatmak için Evet'e tıklayın.\n\nİpucu: Bu pencerede Ctrl+C tüm bilgiyi kopyalar.",
                "RotaLink — Güvenli Uzaktan Destek", YesNoInformation) == 6;
            if (!approved) return;

            using var cancellation = new CancellationTokenSource();
            var remoteSession = RemoteSession.RunAsync(api, session, cancellation.Token);
            var localStop = Task.Run(() => MessageBox(IntPtr.Zero,
                "Bağlantı aktif.\n\nEkranınız destek personeliyle paylaşılıyor. Bağlantıyı sonlandırmak için Tamam veya İptal düğmesine tıklayın.",
                "RotaLink — Bağlantı Aktif", OkCancelInformation));
            await Task.WhenAny(remoteSession, localStop);
            cancellation.Cancel();
            try { await remoteSession; } catch (OperationCanceledException) { }
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, "RotaLink sunucuya bağlanamadı.\n\n" + ex.Message,
                "RotaLink — Bağlantı Hatası", 0x10u | 0x00010000u);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
