using System.Runtime.InteropServices;

namespace RotaLink.SessionHelper;

/// <summary>
/// Keeps the host session awake while the support connection is live. Injected
/// input does not reset the system idle timer, so a long support session could
/// trigger the screen saver, the lock screen or machine sleep (the host reports
/// a 10-minute sleep policy). A dedicated thread re-asserts the execution-state
/// flags periodically; the state dies with the thread.
/// </summary>
internal sealed class SessionKeepAlive : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly HelperLog _log;
    private bool _disposed;

    public SessionKeepAlive(HelperLog log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "RotaLink session keep-alive" };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                lock (_gate)
                {
                    if (_disposed || Monitor.Wait(_gate, 30000)) return;
                }
            }
        }
        catch (Exception exception)
        {
            _log.Write("Session keep-alive stopped unexpectedly: " + exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate) Monitor.PulseAll(_gate);
        SetThreadExecutionState(EsContinuous);
        _thread.Join(2000);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);
}
