using System.Diagnostics;
using System.IO.Pipes;

namespace RemoteSupport.SessionAgent;

internal sealed class SessionHelperVideoClient : IDisposable
{
    private const int HeaderBytes = 40;
    private const int MaximumPayload = 16 * 1024 * 1024;
    private readonly NamedPipeClientStream _pipe;

    private SessionHelperVideoClient(NamedPipeClientStream pipe) => _pipe = pipe;

    public static SessionHelperVideoClient? TryConnect()
    {
        using var process = Process.GetCurrentProcess();
        var pipe = new NamedPipeClientStream(".", "RotaLink.SessionHelper." + process.SessionId + ".Video.v1",
            PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(300);
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
        if (ReadUInt32(header, 0) != 0x564C5452 || ReadUInt16(header, 4) != 1 || header[6] != 3 || header[7] > 1)
            throw new InvalidDataException("Invalid SessionHelper video packet header.");
        var timestamp = BitConverter.ToInt64(header, 16);
        var width = BitConverter.ToInt32(header, 24);
        var height = BitConverter.ToInt32(header, 28);
        var length = BitConverter.ToInt32(header, 32);
        if (width <= 0 || height <= 0 || length is <= 0 or > MaximumPayload)
            throw new InvalidDataException("Invalid SessionHelper video packet dimensions.");
        var payload = new byte[length];
        await ReadExactlyAsync(_pipe, payload, cancellationToken);

        var packet = new byte[14 + payload.Length];
        packet[0] = 4; // RotaLink browser H.264 packet.
        packet[1] = header[7];
        WriteUInt16(packet, 2, checked((ushort)width));
        WriteUInt16(packet, 4, checked((ushort)height));
        Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, packet, 6, 8);
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

    private static ushort ReadUInt16(byte[] data, int offset) => (ushort)(data[offset] | data[offset + 1] << 8);
    private static uint ReadUInt32(byte[] data, int offset) => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
    private static void WriteUInt16(byte[] data, int offset, ushort value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); }
    public void Dispose() => _pipe.Dispose();
}
