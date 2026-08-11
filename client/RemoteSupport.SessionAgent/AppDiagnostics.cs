using System.Text;

namespace RemoteSupport.SessionAgent;

internal static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotaLink");

    public static string LogPath => Path.Combine(DirectoryPath, "rotalink.log");

    public static string CreateSupportBundle()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var bundlePath = Path.Combine(DirectoryPath, "RotaLink-tanilama-tumu.txt");
            using var writer = new StreamWriter(bundlePath, false, new UTF8Encoding(false));
            writer.WriteLine("RotaLink birleşik tanılama kaydı");
            writer.WriteLine("Oluşturulma: " + DateTimeOffset.Now.ToString("O"));
            writer.WriteLine();
            AppendFile(writer, "Kullanıcı uygulaması", LogPath);

            var systemLogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RotaLink", "Logs");
            AppendFile(writer, "SYSTEM servisi", Path.Combine(systemLogDirectory, "Service.log"));
            try
            {
                if (Directory.Exists(systemLogDirectory))
                {
                    foreach (var helperLog in Directory.GetFiles(systemLogDirectory, "SessionHelper-*.log")
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        AppendFile(writer, "Oturum yardımcısı: " + Path.GetFileName(helperLog), helperLog);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                writer.WriteLine("[SYSTEM helper günlükleri okunamadı: " + exception.Message + "]");
            }
            return bundlePath;
        }
    }

    private static void AppendFile(TextWriter writer, string title, string path)
    {
        writer.WriteLine("===== " + title + " =====");
        writer.WriteLine("Dosya: " + path);
        try
        {
            writer.WriteLine(File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "[Dosya bulunamadı]");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writer.WriteLine("[Dosya okunamadı: " + exception.Message + "]");
        }
        writer.WriteLine();
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never interrupt a support session.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write(message + Environment.NewLine + exception);
}
