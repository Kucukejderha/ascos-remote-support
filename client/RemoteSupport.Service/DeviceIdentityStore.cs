using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.Service;

public sealed record DeviceIdentity(string DeviceId, ECDsa SigningKey) : IDisposable
{
    public string PublicKeySpkiBase64 => Convert.ToBase64String(SigningKey.ExportSubjectPublicKeyInfo());
    public void Dispose() => SigningKey.Dispose();
}

[SupportedOSPlatform("windows")]
public sealed class DeviceIdentityStore
{
    private static readonly byte[] Entropy = "ASCOS.RemoteSupport.DeviceIdentity.v1"u8.ToArray();
    private readonly string _identityPath;

    public DeviceIdentityStore(string identityPath) => _identityPath = Path.GetFullPath(identityPath);

    public async Task<DeviceIdentity> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_identityPath)) return await LoadAsync(cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(_identityPath) ?? throw new InvalidOperationException("Identity path has no directory."));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            var protectedKey = WindowsDataProtection.Protect(privateKey, Entropy);
            var document = new StoredIdentity(1, protectedKey);
            var temporaryPath = _identityPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(document), cancellationToken);
            try { File.Move(temporaryPath, _identityPath, overwrite: false); }
            catch (IOException) { File.Delete(temporaryPath); }
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }

        return await LoadAsync(cancellationToken);
    }

    private async Task<DeviceIdentity> LoadAsync(CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(_identityPath, cancellationToken);
        var stored = JsonSerializer.Deserialize<StoredIdentity>(bytes) ?? throw new InvalidDataException("Device identity is invalid.");
        if (stored.Version != 1) throw new InvalidDataException("Unsupported device identity version.");
        var privateKey = WindowsDataProtection.Unprotect(stored.ProtectedPrivateKey, Entropy);
        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(privateKey, out var read);
            if (read != privateKey.Length) { key.Dispose(); throw new InvalidDataException("Device private key contains trailing data."); }
            var deviceId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())[..8]);
            return new(deviceId, key);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    private sealed record StoredIdentity(int Version, byte[] ProtectedPrivateKey);
}
