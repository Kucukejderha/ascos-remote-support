using System.Text.Json;

namespace RemoteSupport.Signaling;

public sealed class AuditLog
{
    private readonly string? _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock;

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
            await File.AppendAllTextAsync(_path, entry, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
