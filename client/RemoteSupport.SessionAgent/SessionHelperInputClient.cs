using System.Diagnostics;
using System.IO.Pipes;

namespace RemoteSupport.SessionAgent;

internal sealed class SessionHelperInputClient : IDisposable
{
    private const int PacketBytes = 40;
    private readonly object _gate = new();
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private long _sequence;
    private int _nextConnectAttempt;
    private bool _unavailableLogged;

    public SessionHelperInputClient()
    {
        using var process = Process.GetCurrentProcess();
        _pipeName = "RotaLink.SessionHelper." + process.SessionId + ".Input.v1";
    }

    public bool? TrySend(InputMessage message)
    {
        lock (_gate)
        {
            if (!EnsureConnected()) return null;
            try
            {
                var sequence = ++_sequence;
                var packet = Encode(message, sequence);
                _pipe!.Write(packet, 0, packet.Length);
                _pipe.Flush();
                var acknowledgement = new byte[9];
                ReadExactly(_pipe, acknowledgement);
                return BitConverter.ToInt64(acknowledgement, 0) == sequence && acknowledgement[8] == 1;
            }
            catch (IOException exception)
            {
                AppDiagnostics.Write("Session helper input pipe disconnected.", exception);
                Disconnect();
                return null;
            }
        }
    }

    private bool EnsureConnected()
    {
        if (_pipe is { IsConnected: true }) return true;
        var now = Environment.TickCount;
        if (unchecked(now - _nextConnectAttempt) < 0) return false;
        _nextConnectAttempt = now + 2000;
        Disconnect();
        var candidate = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.WriteThrough);
        try
        {
            candidate.Connect(150);
            candidate.ReadMode = PipeTransmissionMode.Byte;
            candidate.ReadTimeout = 2000;
            candidate.WriteTimeout = 2000;
            _pipe = candidate;
            _unavailableLogged = false;
            AppDiagnostics.Write("Privileged SessionHelper input IPC connected.");
            return true;
        }
        catch (TimeoutException exception) { candidate.Dispose(); LogUnavailable(exception); return false; }
        catch (IOException exception) { candidate.Dispose(); LogUnavailable(exception); return false; }
    }

    private void LogUnavailable(Exception exception)
    {
        if (_unavailableLogged) return;
        _unavailableLogged = true;
        AppDiagnostics.Write("Privileged SessionHelper input IPC is unavailable. Pipe=" + _pipeName + ".", exception);
    }

    private static byte[] Encode(InputMessage message, long sequence)
    {
        var packet = new byte[PacketBytes];
        WriteUInt32(packet, 0, 0x494C5452);
        WriteUInt16(packet, 4, 1);
        packet[6] = message.Type switch { "move" => 1, "button" => 2, "wheel" => 3, "key" => 4, _ => (byte)0 };
        packet[7] = message.Down ? (byte)1 : (byte)0;
        Buffer.BlockCopy(BitConverter.GetBytes(sequence), 0, packet, 8, 8);
        var x = message.NormalizedX ?? Math.Max(0d, Math.Min(1d, message.X / 65535d));
        var y = message.NormalizedY ?? Math.Max(0d, Math.Min(1d, message.Y / 65535d));
        Buffer.BlockCopy(BitConverter.GetBytes(x), 0, packet, 16, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(y), 0, packet, 24, 8);
        var data = message.Type == "button" ? message.Button : message.Delta;
        Buffer.BlockCopy(BitConverter.GetBytes(data), 0, packet, 32, 4);
        WriteUInt32(packet, 36, WindowsInputDispatcher.MapKey(message.Code));
        return packet;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static void WriteUInt16(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private void Disconnect()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        lock (_gate) Disconnect();
    }
}
