using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace RemoteSupport.SessionAgent;

/// <summary>
/// Checks the server's published client version on startup and, when a newer
/// build is available, downloads it, verifies its SHA-256 against the server
/// manifest, swaps the running executable, and relaunches itself.
/// </summary>
internal static class SelfUpdate
{
    private sealed class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    /// <summary>
    /// Returns true when a newer version was installed and the caller should
    /// exit (the updated executable was already launched).
    /// </summary>
    public static async Task<bool> TryUpdateAsync(Uri baseUri, CancellationToken token)
    {
        try
        {
            var current = CurrentVersion();
            using (var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) })
            {
                var manifest = await DownloadManifestAsync(http, token);
                if (manifest is null) return false;
                if (string.Equals(current, manifest.Version, StringComparison.Ordinal))
                {
                    AppDiagnostics.Write("Self-update: installed version " + current + " is current.");
                    return false;
                }

                AppDiagnostics.Write("Self-update: newer version " + manifest.Version + " found (installed " + current + "); downloading.");
                var temporary = Path.Combine(Path.GetTempPath(), "RotaLink-update-" + Guid.NewGuid().ToString("N") + ".exe");
                try
                {
                    await DownloadFileAsync(http, "/downloads/RotaLink.exe", temporary, token);
                    if (!Sha256Matches(temporary, manifest.Sha256))
                    {
                        AppDiagnostics.Write("Self-update aborted: downloaded file hash does not match the manifest.");
                        return false;
                    }
                    return SwapAndRelaunch(temporary);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { }
                }
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Self-update check failed; continuing with the installed version.", exception);
            return false;
        }
    }

    private static async Task<UpdateManifest?> DownloadManifestAsync(HttpClient http, CancellationToken token)
    {
        using var response = await http.GetAsync("/downloads/version.json", token);
        if (!response.IsSuccessStatusCode) return null;
        var text = (await response.Content.ReadAsStringAsync()).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(text);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Sha256))
            return null;
        return manifest;
    }

    private static async Task DownloadFileAsync(HttpClient http, string path, string destination, CancellationToken token)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync();
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, 81920, token);
    }

    private static bool Sha256Matches(string path, string expectedHex)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return string.Equals(actual, expectedHex.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Moves the running executable aside (Windows allows renaming a running
    /// image), moves the verified download into its place, relaunches with the
    /// original arguments, and reports success so the caller can exit.
    /// </summary>
    private static bool SwapAndRelaunch(string temporary)
    {
        var currentPath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(currentPath)) return false;
        var backupPath = currentPath + ".old";
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(currentPath, backupPath);
            File.Move(temporary, currentPath);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Self-update swap failed.", exception);
            try { if (!File.Exists(currentPath) && File.Exists(backupPath)) File.Move(backupPath, currentPath); }
            catch (IOException) { }
            return false;
        }

        var arguments = Environment.GetCommandLineArgs();
        var serverArgument = arguments.Length > 1 ? arguments[1] : null;
        try
        {
            var startInfo = new ProcessStartInfo(currentPath) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(serverArgument)) startInfo.Arguments = "\"" + serverArgument + "\"";
            Process.Start(startInfo);
            AppDiagnostics.Write("Self-update: swapped to the new build and relaunched.");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Self-update relaunch failed; the new build is in place and will run on the next start.", exception);
        }
        return true;
    }

    private static string CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
}
