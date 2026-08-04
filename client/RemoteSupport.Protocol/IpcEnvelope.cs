using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteSupport.Protocol;

public enum MessageKind : byte { Heartbeat = 1, ConsentRequest = 2, ConsentDecision = 3, SessionStop = 4, Input = 5 }
public sealed record IpcEnvelope(int Version, MessageKind Kind, Guid SessionId, long Sequence, byte[] Payload, byte[] AuthenticationTag);

public static class IpcAuthentication
{
    public const int SessionKeyBytes = 32;
    public const int MaxPayloadBytes = 48 * 1024;

    public static IpcEnvelope Create(MessageKind kind, Guid sessionId, long sequence, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> sessionKey)
    {
        ValidateKey(sessionKey);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (payload.Length > MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        var body = payload.ToArray();
        var tag = ComputeTag(IpcFraming.CurrentVersion, kind, sessionId, sequence, body, sessionKey);
        return new(IpcFraming.CurrentVersion, kind, sessionId, sequence, body, tag);
    }

    public static bool Verify(IpcEnvelope envelope, ReadOnlySpan<byte> sessionKey)
    {
        ValidateKey(sessionKey);
        if (envelope.Version != IpcFraming.CurrentVersion || envelope.Sequence <= 0 || envelope.Payload.Length > MaxPayloadBytes || envelope.AuthenticationTag.Length != 32)
            return false;
        var expected = ComputeTag(envelope.Version, envelope.Kind, envelope.SessionId, envelope.Sequence, envelope.Payload, sessionKey);
        return CryptographicOperations.FixedTimeEquals(expected, envelope.AuthenticationTag);
    }

    private static byte[] ComputeTag(int version, MessageKind kind, Guid sessionId, long sequence, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key)
    {
        var header = new byte[4 + 1 + 16 + 8 + 4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(header, version);
        header[4] = (byte)kind;
        sessionId.TryWriteBytes(header.AsSpan(5, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(21, 8), sequence);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(29, 4), payload.Length);
        payload.CopyTo(header.AsSpan(33));
        return HMACSHA256.HashData(key, header);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != SessionKeyBytes) throw new ArgumentException("IPC session key must be 32 bytes.", nameof(key));
    }
}

public sealed class SequenceGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, long> _lastSeen = new();

    public bool TryAccept(Guid sessionId, long sequence)
    {
        if (sequence <= 0) return false;
        lock (_gate)
        {
            if (_lastSeen.TryGetValue(sessionId, out var last) && sequence <= last) return false;
            _lastSeen[sessionId] = sequence;
            return true;
        }
    }

    public void Remove(Guid sessionId)
    {
        lock (_gate) _lastSeen.Remove(sessionId);
    }
}

public static class IpcFraming
{
    public const int CurrentVersion = 1;
    public const int MaxMessageBytes = 64 * 1024;

    public static async ValueTask WriteAsync(Stream stream, IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJsonContext.Default.IpcEnvelope);
        if (body.Length > MaxMessageBytes) throw new InvalidDataException("IPC message is too large.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, body.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<IpcEnvelope> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length is <= 0 or > MaxMessageBytes) throw new InvalidDataException("Invalid IPC message length.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        var envelope = JsonSerializer.Deserialize(body, ProtocolJsonContext.Default.IpcEnvelope) ?? throw new InvalidDataException("Invalid IPC message.");
        if (envelope.Version != CurrentVersion) throw new InvalidDataException("Unsupported IPC version.");
        if (envelope.Payload.Length > IpcAuthentication.MaxPayloadBytes) throw new InvalidDataException("IPC payload is too large.");
        return envelope;
    }
}

[JsonSerializable(typeof(IpcEnvelope))]
internal partial class ProtocolJsonContext : JsonSerializerContext;
