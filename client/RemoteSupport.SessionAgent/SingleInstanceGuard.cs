using System.Runtime.InteropServices;

namespace RemoteSupport.SessionAgent;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\Rotaniz.RotaLink.Client";
    private const string WindowTitle = "Rotaniz Remote Support — RotaLink";
    private const int SwRestore = 9;
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            var acquired = false;
            try { acquired = mutex.WaitOne(0, false); }
            catch (AbandonedMutexException) { acquired = true; }

            if (acquired) return new SingleInstanceGuard(mutex);
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public static void ActivateExistingWindow()
    {
        var window = FindWindow(null, WindowTitle);
        if (window == IntPtr.Zero) return;
        ShowWindowAsync(window, SwRestore);
        SetForegroundWindow(window);
    }

    public void Dispose()
    {
        if (!_ownsMutex) return;
        _ownsMutex = false;
        try { _mutex.ReleaseMutex(); }
        finally { _mutex.Dispose(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
