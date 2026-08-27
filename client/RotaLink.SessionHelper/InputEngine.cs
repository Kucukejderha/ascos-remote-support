using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RemoteSupport.Protocol;

namespace RotaLink.SessionHelper;

internal sealed class InputEngine : IDisposable
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseVirtualDesktop = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyUp = 0x0002;
    private const uint KeyExtended = 0x0001;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmMiddleDown = 0x0207;
    private const uint WmMiddleUp = 0x0208;
    private const uint WmRightDown = 0x0204;
    private const uint WmRightUp = 0x0205;
    private const uint WmLeftDown = 0x0201;
    private const uint WmLeftUp = 0x0202;
    private bool _fallbackLogged;
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>(), 512);
    private readonly Thread _thread;
    private readonly HelperLog _log;
    private readonly CoordinateTransformationEngine _coordinates = new();

    public InputEngine(HelperLog log)
    {
        _log = log;
        _thread = new Thread(Worker) { IsBackground = true, Name = "RotaLink secure input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public Task<InputInjectionResult> InjectAsync(InputPacket packet, CancellationToken cancellationToken)
    {
        var item = new WorkItem(packet, cancellationToken);
        if (!_queue.TryAdd(item)) return Task.FromResult(InputInjectionResult.Failure(InputFailureStage.QueueFull));
        return item.Completion.Task;
    }

    private void Worker()
    {
        using var desktop = new InputDesktop();
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            try
            {
                item.CancellationToken.ThrowIfCancellationRequested();
                desktop.Refresh();
                var accepted = Inject(item.Packet);
                item.Completion.TrySetResult(accepted
                    ? InputInjectionResult.Success()
                    : InputInjectionResult.Failure(InputFailureStage.PacketInvalid));
            }
            catch (OperationCanceledException)
            {
                item.Completion.TrySetResult(InputInjectionResult.Failure(InputFailureStage.Cancelled));
            }
            catch (Win32Exception exception)
            {
                var stage = exception.Message.StartsWith("OpenInputDesktop", StringComparison.Ordinal)
                    ? InputFailureStage.OpenInputDesktop
                    : exception.Message.StartsWith("SetThreadDesktop", StringComparison.Ordinal)
                        ? InputFailureStage.SetThreadDesktop
                        : InputFailureStage.SendInput;
                _log.Write("Input injection failed. Stage=" + stage + ", Win32Error=" + exception.NativeErrorCode + ". " + exception);
                if (stage == InputFailureStage.SendInput) LogForeground();
                item.Completion.TrySetResult(InputInjectionResult.Failure(stage, exception.NativeErrorCode));
            }
            catch (Exception exception)
            {
                _log.Write("Input injection failed: " + exception);
                item.Completion.TrySetResult(InputInjectionResult.Failure(InputFailureStage.HelperException, exception.HResult));
            }
        }
    }

    private void LogForeground()
    {
        try
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var processId);
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            _log.Write("Foreground window: Process=" + processId + ", Title='" + title + "'.");
        }
        catch (Exception exception)
        {
            _log.Write("Foreground diagnostics failed: " + exception.Message);
        }
    }

    private bool Inject(InputPacket packet)
    {
        var point = _coordinates.Transform(packet.NormalizedX, packet.NormalizedY);
        NativeInput input;
        switch (packet.Kind)
        {
            case InputEventKind.Move:
                input = Mouse(point, MouseMove, 0);
                break;
            case InputEventKind.Button:
                var buttonFlag = (packet.Data, packet.Down) switch
                {
                    (0, true) => MouseLeftDown, (0, false) => MouseLeftUp,
                    (1, true) => MouseMiddleDown, (1, false) => MouseMiddleUp,
                    (2, true) => MouseRightDown, (2, false) => MouseRightUp,
                    _ => 0u
                };
                if (buttonFlag == 0) return false;
                input = Mouse(point, MouseMove | buttonFlag, 0);
                break;
            case InputEventKind.Wheel:
                if (packet.Data is < -1200 or > 1200) return false;
                input = Mouse(point, MouseMove | MouseWheel, unchecked((uint)packet.Data));
                break;
            case InputEventKind.Key:
                if (packet.KeyCode is 0 or > 0xFF) return false;
                var extended = IsExtendedKey(packet.KeyCode) ? KeyExtended : 0u;
                input = new NativeInput
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            VirtualKey = (ushort)packet.KeyCode,
                            Flags = extended | (packet.Down ? 0u : KeyUp)
                        }
                    }
                };
                break;
            default: return false;
        }

        SetLastError(0);
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
        if (sent == 1) return true;

        // Some environments block SendInput (UIPI/desktop restrictions) even
        // for high-integrity callers. Fall back to SetCursorPos, which is not
        // subject to UIPI inside the session, and to window messages.
        if (TryFallback(packet, point)) return true;

        throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput injected no events. This usually indicates UIPI or desktop-token mismatch.");
    }

    private bool TryFallback(InputPacket packet, VirtualDesktopPoint point)
    {
        switch (packet.Kind)
        {
            case InputEventKind.Move:
                if (SetCursorPos(point.PixelX, point.PixelY))
                {
                    LogFallback("SetCursorPos move");
                    return true;
                }
                return false;
            case InputEventKind.Button:
                {
                    var message = (packet.Data, packet.Down) switch
                    {
                        (0, true) => WmLeftDown, (0, false) => WmLeftUp,
                        (1, true) => WmMiddleDown, (1, false) => WmMiddleUp,
                        (2, true) => WmRightDown, (2, false) => WmRightUp,
                        _ => 0u
                    };
                    if (message != 0 && PostToWindow(point, message, 0))
                    {
                        LogFallback("PostMessage button");
                        return true;
                    }
                    return false;
                }
            case InputEventKind.Wheel:
                if (PostToWindow(point, WmMouseWheel, unchecked((uint)(packet.Data << 16))))
                {
                    LogFallback("PostMessage wheel");
                    return true;
                }
                return false;
            case InputEventKind.Key:
                {
                    var target = GetForegroundWindow();
                    if (target != IntPtr.Zero && PostMessage(target, packet.Down ? WmKeyDown : WmKeyUp, new IntPtr(unchecked((long)packet.KeyCode)), IntPtr.Zero))
                    {
                        LogFallback("PostMessage key");
                        return true;
                    }
                    return false;
                }
            default:
                return false;
        }
    }

    private bool PostToWindow(VirtualDesktopPoint point, uint message, uint wParam)
    {
        var target = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
        if (target == IntPtr.Zero) return false;
        var lParam = new IntPtr((point.PixelY << 16) | (point.PixelX & 0xFFFF));
        return PostMessage(target, message, new IntPtr(unchecked((long)wParam)), lParam);
    }

    private void LogFallback(string mechanism)
    {
        if (_fallbackLogged) return;
        _fallbackLogged = true;
        _log.Write("SendInput is blocked in this session; using the " + mechanism + " fallback.");
    }

    private static NativeInput Mouse(VirtualDesktopPoint point, uint flags, uint data) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput
            {
                X = point.AbsoluteX,
                Y = point.AbsoluteY,
                MouseData = data,
                Flags = MouseAbsolute | MouseVirtualDesktop | flags
            }
        }
    };

    private static bool IsExtendedKey(uint key) => key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x6F or 0x90 or 0x91;

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(5))) _log.Write("Input thread did not stop in time.");
        while (_queue.TryTake(out var item)) item.Completion.TrySetCanceled();
        _queue.Dispose();
    }

    private sealed class WorkItem
    {
        public WorkItem(InputPacket packet, CancellationToken cancellationToken)
        {
            Packet = packet;
            CancellationToken = cancellationToken;
        }

        public InputPacket Packet { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<InputInjectionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint errorCode);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }
}

internal enum InputFailureStage : byte
{
    None = 0,
    SequenceRejected = 1,
    QueueFull = 2,
    OpenInputDesktop = 3,
    SetThreadDesktop = 4,
    SendInput = 5,
    PacketInvalid = 6,
    HelperException = 7,
    Cancelled = 8
}

internal readonly struct InputInjectionResult
{
    private InputInjectionResult(bool accepted, InputFailureStage stage, int errorCode)
    {
        Accepted = accepted;
        Stage = stage;
        ErrorCode = errorCode;
    }

    public bool Accepted { get; }
    public InputFailureStage Stage { get; }
    public int ErrorCode { get; }

    public static InputInjectionResult Success() => new(true, InputFailureStage.None, 0);
    public static InputInjectionResult Failure(InputFailureStage stage, int errorCode = 0) => new(false, stage, errorCode);
}
