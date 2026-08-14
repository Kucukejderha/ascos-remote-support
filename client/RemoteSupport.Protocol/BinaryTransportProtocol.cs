using System.Buffers.Binary;

namespace RemoteSupport.Protocol;

public enum InputEventKind : byte { Move = 1, Button = 2, Wheel = 3, Key = 4, Click = 5 }
public enum VideoCodec : byte { Bgra32 = 1, Nv12 = 2, H264AnnexB = 3 }

public readonly record struct InputPacket(
    InputEventKind Kind,
    bool Down,
    long Sequence,
    double NormalizedX,
    double NormalizedY,
    int Data,
    uint KeyCode);

public readonly record struct VideoPacketHeader(
    VideoCodec Codec,
    bool KeyFrame,
    long Sequence,
    long Timestamp100Nanoseconds,
    int Width,
    int Height,
    int PayloadLength);

public static class InputPacketCodec
{
    public const int PacketBytes = 40;
    private const uint Magic = 0x494C5452; // RTLI in little-endian byte order
    private const ushort Version = 1;

    public static void Write(Span<byte> destination, in InputPacket packet)
    {
        if (destination.Length < PacketBytes) throw new ArgumentException("Input packet buffer is too small.", nameof(destination));
        if (packet.Sequence <= 0) throw new ArgumentOutOfRangeException(nameof(packet));
        if (!Enum.IsDefined(typeof(InputEventKind), packet.Kind)) throw new ArgumentOutOfRangeException(nameof(packet));
        ValidateNormalized(packet.NormalizedX, nameof(packet.NormalizedX));
        ValidateNormalized(packet.NormalizedY, nameof(packet.NormalizedY));
        destination[..PacketBytes].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Version);
        destination[6] = (byte)packet.Kind;
        destination[7] = packet.Down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], packet.Sequence);
        BinaryPrimitives.WriteInt64LittleEndian(destination[16..], BitConverter.DoubleToInt64Bits(packet.NormalizedX));
        BinaryPrimitives.WriteInt64LittleEndian(destination[24..], BitConverter.DoubleToInt64Bits(packet.NormalizedY));
        BinaryPrimitives.WriteInt32LittleEndian(destination[32..], packet.Data);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], packet.KeyCode);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out InputPacket packet)
    {
        packet = default;
        if (source.Length != PacketBytes || BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) != Version) return false;
        var kind = (InputEventKind)source[6];
        var flags = source[7];
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(source[8..]);
        var x = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(source[16..]));
        var y = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(source[24..]));
        if (!Enum.IsDefined(typeof(InputEventKind), kind) || flags > 1 || sequence <= 0 ||
            !IsNormalized(x) || !IsNormalized(y)) return false;
        packet = new InputPacket(kind, flags != 0, sequence, x, y,
            BinaryPrimitives.ReadInt32LittleEndian(source[32..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[36..]));
        return true;
    }

    private static void ValidateNormalized(double value, string name)
    {
        if (!IsNormalized(value)) throw new ArgumentOutOfRangeException(name);
    }

    private static bool IsNormalized(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0d and <= 1d;
}

public static class VideoPacketCodec
{
    public const int HeaderBytes = 40;
    public const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private const uint Magic = 0x564C5452; // RTLV
    private const ushort Version = 1;

    public static void WriteHeader(Span<byte> destination, in VideoPacketHeader header)
    {
        if (destination.Length < HeaderBytes) throw new ArgumentException("Video header buffer is too small.", nameof(destination));
        if (!Enum.IsDefined(typeof(VideoCodec), header.Codec) || header.Sequence <= 0 ||
            header.Width <= 0 || header.Height <= 0 || header.PayloadLength is < 0 or > MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(header));
        destination[..HeaderBytes].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Version);
        destination[6] = (byte)header.Codec;
        destination[7] = header.KeyFrame ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], header.Sequence);
        BinaryPrimitives.WriteInt64LittleEndian(destination[16..], header.Timestamp100Nanoseconds);
        BinaryPrimitives.WriteInt32LittleEndian(destination[24..], header.Width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[28..], header.Height);
        BinaryPrimitives.WriteInt32LittleEndian(destination[32..], header.PayloadLength);
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> source, out VideoPacketHeader header)
    {
        header = default;
        if (source.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) != Version) return false;
        var codec = (VideoCodec)source[6];
        var flags = source[7];
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(source[8..]);
        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(source[16..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(source[24..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(source[28..]);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source[32..]);
        if (!Enum.IsDefined(typeof(VideoCodec), codec) || flags > 1 || sequence <= 0 || width <= 0 || height <= 0 ||
            payloadLength is < 0 or > MaximumPayloadBytes) return false;
        header = new VideoPacketHeader(codec, flags != 0, sequence, timestamp, width, height, payloadLength);
        return true;
    }
}
