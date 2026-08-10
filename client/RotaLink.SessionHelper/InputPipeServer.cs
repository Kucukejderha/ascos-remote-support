using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RemoteSupport.Protocol;

namespace RotaLink.SessionHelper;

internal sealed class InputPipeServer
{
    private readonly uint _sessionId;
    private readonly InputEngine _engine;
    private readonly HelperLog _log;

    public InputPipeServer(uint sessionId, InputEngine engine, HelperLog log)
    {
        _sessionId = sessionId;
        _engine = engine;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe();
            _log.Write("Waiting for input IPC client on " + PipeName + ".");
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            _log.Write("Input IPC client connected.");
            try { await ProcessClientAsync(pipe, cancellationToken).ConfigureAwait(false); }
            catch (EndOfStreamException) { }
            catch (IOException exception) { _log.Write("Input IPC disconnected: " + exception.Message); }
        }
    }

    private async Task ProcessClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        var packetBytes = new byte[InputPacketCodec.PacketBytes];
        var acknowledgement = new byte[9];
        while (!cancellationToken.IsCancellationRequested)
        {
            await stream.ReadExactlyAsync(packetBytes, cancellationToken).ConfigureAwait(false);
            if (!InputPacketCodec.TryRead(packetBytes, out var packet))
                throw new InvalidDataException("Malformed RotaLink input packet.");
            var accepted = await _engine.InjectAsync(packet, cancellationToken).ConfigureAwait(false);
            BinaryPrimitives.WriteInt64LittleEndian(acknowledgement, packet.Sequence);
            acknowledgement[8] = accepted ? (byte)1 : (byte)0;
            await stream.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(GetInteractiveUserSid(),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough, 4096, 4096, security,
            HandleInheritability.None, PipeAccessRights.ChangePermissions);
    }

    private SecurityIdentifier GetInteractiveUserSid()
    {
        if (!WTSQueryUserToken(_sessionId, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WTSQueryUserToken failed.");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User ?? throw new InvalidOperationException("Interactive token has no user SID.");
    }

    private string PipeName => "RotaLink.SessionHelper." + _sessionId + ".Input.v1";

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
}
