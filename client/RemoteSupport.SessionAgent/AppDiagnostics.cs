using System.Text;

namespace RemoteSupport.SessionAgent;

internal static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotaLink");

    public static string LogPath => Path.Combine(DirectoryPath, "rotalink.log");

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
