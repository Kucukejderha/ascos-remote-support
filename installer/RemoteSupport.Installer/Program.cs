using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

const string serverUrl = "https://45.87.173.201.nip.io";
var testRoot = Environment.GetEnvironmentVariable("ASCOS_INSTALL_TEST_ROOT");
var installDirectory = testRoot is null
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rotaniz", "RotaLink")
    : Path.Combine(testRoot, "app");
var shortcutDirectory = testRoot is null
    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
    : Path.Combine(testRoot, "desktop");

try
{
    Directory.CreateDirectory(installDirectory);
    var assembly = Assembly.GetExecutingAssembly();
    await using var payload = assembly.GetManifestResourceStream("RotaLink.HostPayload.zip")
        ?? throw new InvalidOperationException("Kurulum paketi bulunamadı.");
    using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
    foreach (var entry in archive.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name)) continue;
        var destination = Path.GetFullPath(Path.Combine(installDirectory, entry.FullName));
        if (!destination.StartsWith(Path.GetFullPath(installDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Geçersiz kurulum dosyası yolu.");
        entry.ExtractToFile(destination, overwrite: true);
    }

    var executable = Path.Combine(installDirectory, "RotaLink.exe");
    if (!File.Exists(executable)) throw new FileNotFoundException("Uygulama dosyası kurulamadı.", executable);
    Directory.CreateDirectory(shortcutDirectory);
    CreateDesktopShortcut(executable, serverUrl, installDirectory, shortcutDirectory);

    if (testRoot is not null) return 0;
    var answer = MessageBox(IntPtr.Zero,
        "Rotaniz Remote Support başarıyla kuruldu.\n\nMasaüstüne 'RotaLink' kısayolu eklendi.\n\nUygulama şimdi açılsın mı?",
        "Rotaniz Remote Support", 0x00000004u | 0x00000040u | 0x00001000u);
    if (answer == 6)
        Process.Start(new ProcessStartInfo(executable, $"\"{serverUrl}\"") { WorkingDirectory = installDirectory, UseShellExecute = true });
    return 0;
}
catch (Exception exception)
{
    MessageBox(IntPtr.Zero,
        $"Kurulum tamamlanamadı.\n\n{exception.Message}\n\nUygulama açıksa kapatıp kurulumu yeniden deneyin.",
        "Rotaniz Remote Support — Kurulum Hatası", 0x00000010u | 0x00001000u);
    return 1;
}

static void CreateDesktopShortcut(string executable, string serverUrl, string workingDirectory, string shortcutDirectory)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows kısayol servisi bulunamadı.");
    dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows kısayol servisi başlatılamadı.");
    dynamic shortcut = shell.CreateShortcut(Path.Combine(shortcutDirectory, "RotaLink.lnk"));
    shortcut.TargetPath = executable;
    shortcut.Arguments = $"\"{serverUrl}\"";
    shortcut.WorkingDirectory = workingDirectory;
    shortcut.Description = "Rotaniz Remote Support";
    shortcut.Save();
    Marshal.FinalReleaseComObject(shortcut);
    Marshal.FinalReleaseComObject(shell);
}

[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
static extern int MessageBox(IntPtr window, string text, string caption, uint type);
