using System.Text.Json;

namespace RemoteSupport.Signaling;

public sealed class AuditLog
{
    private const long RotationBytes = 10 * 1024 * 1024;
    private const int RotationCheckIntervalWrites = 100;
    private readonly string? _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock;
    private long _writesSinceRotationCheck;

    public AuditLog(IConfiguration configuration, TimeProvider clock)
    {
        _path = configuration["AUDIT_LOG_PATH"];
        _clock = clock;
    }

    public async Task WriteAsync(string eventName, string? deviceId, string? sessionId, string? remoteAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_path)) return;
        var entry = JsonSerializer.Serialize(new { timestamp = _clock.GetUtcNow(), eventName, deviceId, sessionId, remoteAddress }) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (++_writesSinceRotationCheck >= RotationCheckIntervalWrites)
            {
                _writesSinceRotationCheck = 0;
                TryRotateIfLarge();
            }
            await File.AppendAllTextAsync(_path, entry, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private void TryRotateIfLarge()
    {
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length <= RotationBytes) return;
            File.Move(_path, _path + "." + _clock.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
