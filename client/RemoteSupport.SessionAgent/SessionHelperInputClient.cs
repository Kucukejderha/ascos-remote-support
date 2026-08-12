using System.Diagnostics;
using System.IO.Pipes;

namespace RemoteSupport.SessionAgent;

internal sealed class SessionHelperInputClient : IDisposable
{
    private const int PacketBytes = 40;
    private const int AcknowledgementBytes = 24;
    private const uint AcknowledgementMagic = 0x4F4C5452;
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

    public SessionHelperInputResult? TrySend(InputMessage message)
    {
        lock (_gate)
        {
            if (message.Type == "key" && WindowsInputDispatcher.MapKey(message.Code) == 0)
                return new SessionHelperInputResult(false, "unsupported-key", 0);
            if (!EnsureConnected()) return null;
            try
            {
                var sequence = ++_sequence;
                var packet = Encode(message, sequence);
                _pipe!.Write(packet, 0, packet.Length);
                _pipe.Flush();
                var acknowledgement = new byte[AcknowledgementBytes];
                ReadExactly(_pipe, acknowledgement);
                if (BitConverter.ToUInt32(acknowledgement, 0) != AcknowledgementMagic ||
                    BitConverter.ToUInt16(acknowledgement, 4) != 2 ||
                    BitConverter.ToInt64(acknowledgement, 8) != sequence)
                    throw new InvalidDataException("Session helper returned an invalid acknowledgement.");
                return new SessionHelperInputResult(
                    acknowledgement[6] == 1,
                    MapStage(acknowledgement[7]),
                    BitConverter.ToInt32(acknowledgement, 16));
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException)
            {
                AppDiagnostics.Write("Session helper input pipe disconnected.", exception);
                Disconnect();
                return null;
            }
        }
    }

    private static string MapStage(byte stage) => stage switch
    {
        0 => "sendinput-ok",
        1 => "sequence-rejected",
        2 => "queue-full",
        3 => "open-input-desktop-failed",
        4 => "set-thread-desktop-failed",
        5 => "sendinput-failed",
        6 => "packet-invalid",
        7 => "helper-exception",
        8 => "helper-cancelled",
        _ => "helper-stage-unknown"
    };

    private bool EnsureConnected()
    {
        if (_pipe is { IsConnected: true }) return true;
        var now = Environment.TickCount;
        if (unchecked(now - _nextConnectAttempt) < 0) return false;
        _nextConnectAttempt = now + 2000;
        Disconnect();
        var candidate = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        try
        {
            candidate.Connect(150);
            candidate.ReadMode = PipeTransmissionMode.Byte;
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
            var pending = stream.BeginRead(buffer, offset, buffer.Length - offset, null, null);
            int read;
            try
            {
                if (!pending.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("Session helper acknowledgement timed out after 2 seconds.");
                read = stream.EndRead(pending);
            }
            finally
            {
                pending.AsyncWaitHandle.Dispose();
            }
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

internal readonly struct SessionHelperInputResult
{
    public SessionHelperInputResult(bool accepted, string stage, int errorCode)
    {
        Accepted = accepted;
        Stage = stage;
        ErrorCode = errorCode;
    }

    public bool Accepted { get; }
    public string Stage { get; }
    public int ErrorCode { get; }
}
