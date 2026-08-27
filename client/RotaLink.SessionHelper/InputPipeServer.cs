using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RemoteSupport.Protocol;

namespace RotaLink.SessionHelper;

internal sealed class InputPipeServer
{
    private const int PacketBytes = 40;
    private const int AcknowledgementBytes = 24;
    private const uint AcknowledgementMagic = 0x4F4C5452;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private readonly uint _sessionId;
    private readonly InputEngine _engine;
    private readonly HelperLog _log;

    public InputPipeServer(uint sessionId, InputEngine engine, HelperLog log)
    {
        _sessionId = sessionId;
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
            _log.Write("Input IPC client connected.");
            try
            {
                VerifyClientIdentity(pipe);
                ProcessClient(pipe, stop);
            }
            catch (EndOfStreamException) { }
            catch (IOException exception) { _log.Write("Input IPC disconnected: " + exception.Message); }
            catch (InvalidDataException exception) { _log.Write("Input IPC client rejected: " + exception.Message); }
        }
    }

    /// <summary>
    /// Authenticates the pipe client before any input is accepted. The named
    /// pipe ACL already restricts connections to the interactive user and
    /// SYSTEM; this additionally verifies that the connected process runs in
    /// the helper's session. Process image verification is best-effort: the
    /// unelevated helper cannot open an elevated RotaLink.exe (integrity
    /// level), so a failed open is accepted instead of breaking control.
    /// </summary>
    private void VerifyClientIdentity(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId) || clientProcessId == 0)
            throw new InvalidDataException("Pipe client process could not be resolved.");
        if (!ProcessIdToSessionId(clientProcessId, out var clientSession) || clientSession != _sessionId)
            throw new InvalidDataException("Pipe client is not in the interactive session.");

        using (var process = OpenProcess(ProcessQueryLimitedInformation, false, clientProcessId))
        {
            if (process.IsInvalid)
            {
                _log.Write("Input IPC client process could not be opened for image verification; accepting based on pipe ACL and session match.");
                return;
            }
            var image = new StringBuilder(512);
            var imageLength = image.Capacity;
            if (!QueryFullProcessImageName(process, 0, image, ref imageLength)) return;
            var fileName = Path.GetFileName(image.ToString());
            if (!fileName.StartsWith("RotaLink", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Untrusted pipe client image: " + fileName);
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
            if (!InputPacketCodec.TryRead(packetBytes, out var packet))
                throw new InvalidDataException("Malformed RotaLink input packet.");
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

    private void ReadExactly(Stream stream, byte[] buffer)
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
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint flags, StringBuilder imageName, ref int size);
}
