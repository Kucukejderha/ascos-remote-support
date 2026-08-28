namespace RemoteSupport.SessionAgent;

/// <summary>
/// Shared state flag for the input runtime (either the SYSTEM broker service
/// or the elevated helper). Non-zero means the privileged input path is live.
/// </summary>
internal static class InputRuntime
{
    public static int IsRunning;
}
