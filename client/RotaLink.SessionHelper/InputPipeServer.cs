using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RotaLink.SessionHelper;

internal sealed class InputPipeServer
{
    private const int PacketBytes = 40;
    private const int AcknowledgementBytes = 24;
    private const uint AcknowledgementMagic = 0x4F4C5452;
    private readonly uint _sessionId;
    private readonly uint _allowedClientProcessId;
    private readonly InputEngine _engine;
    private readonly HelperLog _log;

    public InputPipeServer(uint sessionId, uint allowedClientProcessId, InputEngine engine, HelperLog log)
    {
        _sessionId = sessionId;
        _allowedClientProcessId = allowedClientProcessId;
        _engine = engine;
        _log = log;
    }

    public void Run(EventWaitHandle stop)
    {
        while (!stop.WaitOne(0))
        {
            using var pipe = CreatePipe();
            _log.Write("Waiting for input IPC client on " + PipeName + ".");
            var pending = pipe.BeginWaitForConnection(null, null);
            if (WaitHandle.WaitAny(new[] { pending.AsyncWaitHandle, stop }) == 1) return;
            pipe.EndWaitForConnection(pending);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientProcessId failed.");
            if (clientProcessId != _allowedClientProcessId)
            {
                _log.Write("Rejected input IPC client process " + clientProcessId +
                    "; expected " + _allowedClientProcessId + ".");
                pipe.Disconnect();
                continue;
            }
            _log.Write("Input IPC client connected.");
            try { ProcessClient(pipe, stop); }
            catch (EndOfStreamException) { }
            catch (IOException exception) { _log.Write("Input IPC disconnected: " + exception.Message); }
        }
    }

    private void ProcessClient(Stream stream, EventWaitHandle stop)
    {
        var packetBytes = new byte[PacketBytes];
        var acknowledgement = new byte[AcknowledgementBytes];
        long lastSequence = 0;
        while (!stop.WaitOne(0))
        {
            ReadExactly(stream, packetBytes);
            if (!TryDecode(packetBytes, out var packet)) throw new InvalidDataException("Malformed RotaLink input packet.");
            var result = packet.Sequence <= lastSequence
                ? InputInjectionResult.Failure(InputFailureStage.SequenceRejected)
                : _engine.InjectAsync(packet, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Accepted) lastSequence = packet.Sequence;
            EncodeAcknowledgement(acknowledgement, packet.Sequence, result);
            stream.Write(acknowledgement, 0, acknowledgement.Length);
            stream.Flush();
        }
    }

    private static void EncodeAcknowledgement(byte[] target, long sequence, InputInjectionResult result)
    {
        Array.Clear(target, 0, target.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(AcknowledgementMagic), 0, target, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)2), 0, target, 4, 2);
        target[6] = result.Accepted ? (byte)1 : (byte)0;
        target[7] = (byte)result.Stage;
        Buffer.BlockCopy(BitConverter.GetBytes(sequence), 0, target, 8, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(result.ErrorCode), 0, target, 16, 4);
    }

    private static bool TryDecode(byte[] source, out InputPacket packet)
    {
        packet = null!;
        if (source.Length != PacketBytes || BitConverter.ToUInt32(source, 0) != 0x494C5452 || BitConverter.ToUInt16(source, 4) != 1) return false;
        var kind = (InputEventKind)source[6];
        var flags = source[7];
        var sequence = BitConverter.ToInt64(source, 8);
        var x = BitConverter.ToDouble(source, 16);
        var y = BitConverter.ToDouble(source, 24);
        if (kind < InputEventKind.Move || kind > InputEventKind.Click || flags > 1 || sequence <= 0 ||
            double.IsNaN(x) || double.IsInfinity(x) || x < 0 || x > 1 ||
            double.IsNaN(y) || double.IsInfinity(y) || y < 0 || y > 1) return false;
        packet = new InputPacket(kind, flags != 0, sequence, x, y, BitConverter.ToInt32(source, 32), BitConverter.ToUInt32(source, 36));
        return true;
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

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(GetInteractiveUserSid(), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough, 4096, 4096, security);
    }

    private SecurityIdentifier GetInteractiveUserSid()
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        if (!currentIdentity.IsSystem)
            return currentIdentity.User ?? throw new InvalidOperationException("Interactive helper token has no user SID.");

        if (!WTSQueryUserToken(_sessionId, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User ?? throw new InvalidOperationException("Interactive token has no user SID.");
    }

    private string PipeName => "RotaLink.SessionHelper." + _sessionId + ".Input.v1";

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
