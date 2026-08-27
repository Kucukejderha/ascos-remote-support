using System.Diagnostics;
using System.IO.Pipes;
using RemoteSupport.Protocol;

namespace RemoteSupport.SessionAgent;

internal sealed class SessionHelperVideoClient : IDisposable
{
    private const int HeaderBytes = 40;
    private readonly NamedPipeClientStream _pipe;

    private SessionHelperVideoClient(NamedPipeClientStream pipe) => _pipe = pipe;

    public static SessionHelperVideoClient? TryConnect()
    {
        using var process = Process.GetCurrentProcess();
        var pipe = new NamedPipeClientStream(".", "RotaLink.SessionHelper." + process.SessionId + ".Video.v1",
            PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(2000);
            AppDiagnostics.Write("DXGI/H.264 SessionHelper video IPC connected.");
            return new SessionHelperVideoClient(pipe);
        }
        catch (TimeoutException) { pipe.Dispose(); return null; }
        catch (IOException) { pipe.Dispose(); return null; }
    }

    public async Task<byte[]> ReadWebSocketPacketAsync(CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];
        await ReadExactlyAsync(_pipe, header, cancellationToken);
        if (!VideoPacketCodec.TryReadHeader(header, out var decoded) || decoded.Codec != VideoCodec.H264AnnexB)
            throw new InvalidDataException("Invalid SessionHelper video packet header.");
        if (decoded.PayloadLength is <= 0 or > VideoPacketCodec.MaximumPayloadBytes)
            throw new InvalidDataException("Invalid SessionHelper video packet payload length.");
        var payload = new byte[decoded.PayloadLength];
        await ReadExactlyAsync(_pipe, payload, cancellationToken);

        var packet = new byte[14 + payload.Length];
        packet[0] = 4; // RotaLink browser H.264 packet.
        packet[1] = decoded.KeyFrame ? (byte)1 : (byte)0;
        WriteUInt16(packet, 2, checked((ushort)decoded.Width));
        WriteUInt16(packet, 4, checked((ushort)decoded.Height));
        Buffer.BlockCopy(BitConverter.GetBytes(decoded.Timestamp100Nanoseconds), 0, packet, 6, 8);
        Buffer.BlockCopy(payload, 0, packet, 14, payload.Length);
        return packet;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); }
    public void Dispose() => _pipe.Dispose();
}
