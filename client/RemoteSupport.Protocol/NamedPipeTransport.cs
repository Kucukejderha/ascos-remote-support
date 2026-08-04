using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace RemoteSupport.Protocol;

public static class NamedPipeTransport
{
    public const string DefaultPipeName = "ascos.remote-support.session-agent.v1";

    public static NamedPipeServerStream CreateCurrentUserServer(string pipeName = DefaultPipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            IpcFraming.MaxMessageBytes, IpcFraming.MaxMessageBytes);

    public static NamedPipeClientStream CreateCurrentUserClient(string pipeName = DefaultPipeName) =>
        new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    [SupportedOSPlatform("windows")]
    public static NamedPipeServerStream CreateServiceToUserServer(SecurityIdentifier interactiveUserSid, string pipeName = DefaultPipeName)
    {
        ArgumentNullException.ThrowIfNull(interactiveUserSid);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            interactiveUserSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            IpcFraming.MaxMessageBytes,
            IpcFraming.MaxMessageBytes,
            security,
            HandleInheritability.None,
            PipeAccessRights.ChangePermissions);
    }
}
