using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace RemoteSupport.Protocol;

[SupportedOSPlatform("windows")]
public sealed record DeviceIdentity(string DeviceId, ECDsa SigningKey) : IDisposable
{
    public string PublicKeySpkiBase64 => Convert.ToBase64String(DeviceIdentityStore.ExportSpki(SigningKey));
    public void Dispose() => SigningKey.Dispose();
}

/// <summary>
/// Persists the device signing key with Windows DPAPI so the same device
/// identity is reused across application launches. The stored format is a
/// compact binary envelope that works on .NET Framework 4.8 and .NET 8.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DeviceIdentityStore
{
    private const int CurrentVersion = 1;
    private static readonly byte[] Entropy = "ASCOS.RemoteSupport.DeviceIdentity.v1"u8.ToArray();
    private readonly string _identityPath;

    public DeviceIdentityStore(string identityPath) => _identityPath = Path.GetFullPath(identityPath);

    public Task<DeviceIdentity> LoadOrCreateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LoadOrCreate(cancellationToken));

    private DeviceIdentity LoadOrCreate(CancellationToken cancellationToken)
    {
        if (File.Exists(_identityPath)) return Load(cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(_identityPath) ?? throw new InvalidOperationException("Identity path has no directory."));
        using var key = CreateKey();
        var privateKey = ExportPkcs8(key);
        try
        {
            var protectedKey = WindowsDataProtection.Protect(privateKey, Entropy);
            var document = Serialize(CurrentVersion, protectedKey);
            var temporaryPath = _identityPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporaryPath, document);
            try { File.Move(temporaryPath, _identityPath); }
            catch (IOException) { File.Delete(temporaryPath); }
        }
        finally { Array.Clear(privateKey, 0, privateKey.Length); }

        return Load(cancellationToken);
    }

    private DeviceIdentity Load(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(_identityPath);
        if (!TryDeserialize(bytes, out var version, out var protectedKey))
            throw new InvalidDataException("Device identity is invalid.");
        if (version != CurrentVersion) throw new InvalidDataException("Unsupported device identity version.");
        var privateKey = WindowsDataProtection.Unprotect(protectedKey, Entropy);
        try
        {
            var key = ImportPkcs8(privateKey);
            var fingerprint = Sha256(ExportSpki(key));
            var deviceId = HexString(fingerprint, 8);
            return new(deviceId, key);
        }
        finally { Array.Clear(privateKey, 0, privateKey.Length); }
    }

    private static ECDsaCng CreateKey()
    {
        using var cngKey = CngKey.Create(CngAlgorithm.ECDsaP256, null, new CngKeyCreationParameters
        {
            ExportPolicy = CngExportPolicies.AllowPlaintextExport,
            KeyUsage = CngKeyUsages.Signing
        });
        return new ECDsaCng(cngKey);
    }

    private static byte[] ExportPkcs8(ECDsaCng key) => key.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);

    internal static byte[] ExportSpki(ECDsa key)
    {
        var cng = key as ECDsaCng ?? throw new PlatformNotSupportedException("Windows ECDSA key required.");
        var blob = cng.Key.Export(CngKeyBlobFormat.EccPublicBlob);
        if (blob.Length != 72 || BitConverter.ToInt32(blob, 4) != 32)
            throw new CryptographicException("Unexpected P-256 key.");
        byte[] prefix = { 0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04 };
        var spki = new byte[91];
        Buffer.BlockCopy(prefix, 0, spki, 0, prefix.Length);
        Buffer.BlockCopy(blob, 8, spki, prefix.Length, 64);
        return spki;
    }

    private static ECDsaCng ImportPkcs8(byte[] pkcs8)
    {
        using var imported = CngKey.Import(pkcs8, CngKeyBlobFormat.Pkcs8PrivateBlob);
        return new ECDsaCng(imported);
    }

    private static byte[] Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    private static string HexString(byte[] data, int length)
    {
        var result = new System.Text.StringBuilder(length * 2);
        for (var i = 0; i < length; i++) result.Append(data[i].ToString("X2"));
        return result.ToString();
    }

    private static byte[] Serialize(int version, byte[] protectedKey)
    {
        var payload = new byte[1 + 4 + protectedKey.Length];
        payload[0] = (byte)version;
        WriteInt32BigEndian(payload, 1, protectedKey.Length);
        Buffer.BlockCopy(protectedKey, 0, payload, 5, protectedKey.Length);
        return payload;
    }

    private static bool TryDeserialize(byte[] payload, out int version, out byte[] protectedKey)
    {
        version = 0;
        protectedKey = Array.Empty<byte>();
        if (payload.Length < 5) return false;
        version = payload[0];
        var length = ReadInt32BigEndian(payload, 1);
        if (length < 0 || 5 + length > payload.Length) return false;
        protectedKey = new byte[length];
        Buffer.BlockCopy(payload, 5, protectedKey, 0, length);
        return true;
    }

    private static void WriteInt32BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static int ReadInt32BigEndian(byte[] source, int offset) =>
        source[offset] << 24 | source[offset + 1] << 16 | source[offset + 2] << 8 | source[offset + 3];
}
