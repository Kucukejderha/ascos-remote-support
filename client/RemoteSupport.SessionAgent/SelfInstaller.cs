using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteSupport.SessionAgent;

[SupportedOSPlatform("windows")]
public static class SelfInstaller
{
    private const string ServerUrl = "https://45.87.173.201.nip.io";

    public static int Install()
    {
        try
        {
            var testRoot = Environment.GetEnvironmentVariable("ASCOS_INSTALL_TEST_ROOT");
            var installDirectory = testRoot is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rotaniz", "RotaLink")
                : Path.Combine(testRoot, "app");
            var shortcutDirectory = testRoot is null
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : Path.Combine(testRoot, "desktop");
            Directory.CreateDirectory(installDirectory);
            Directory.CreateDirectory(shortcutDirectory);

            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory))
            {
                var extension = Path.GetExtension(source);
                if (extension is not (".exe" or ".dll" or ".json")) continue;
                File.Copy(source, Path.Combine(installDirectory, Path.GetFileName(source)), overwrite: true);
            }

            var executable = Path.Combine(installDirectory, "RotaLink.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Kurulan uygulama dosyası bulunamadı.", executable);
            CreateShortcut(executable, installDirectory, shortcutDirectory);

            if (testRoot is null)
            {
                MessageBox(IntPtr.Zero, "Rotaniz Remote Support başarıyla kuruldu. Masaüstü kısayolu oluşturuldu.", "Rotaniz Remote Support", 0x40u | 0x1000u);
                Process.Start(new ProcessStartInfo(executable, $"\"{ServerUrl}\"") { WorkingDirectory = installDirectory, UseShellExecute = true });
            }
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox(IntPtr.Zero, $"Kurulum tamamlanamadı.\n\n{exception.Message}", "Rotaniz Remote Support — Kurulum Hatası", 0x10u | 0x1000u);
            return 1;
        }
    }

    private static void CreateShortcut(string executable, string workingDirectory, string shortcutDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows kısayol servisi bulunamadı.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows kısayol servisi başlatılamadı.");
        dynamic shortcut = shell.CreateShortcut(Path.Combine(shortcutDirectory, "RotaLink.lnk"));
        shortcut.TargetPath = executable;
        shortcut.Arguments = $"\"{ServerUrl}\"";
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "Rotaniz Remote Support";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
