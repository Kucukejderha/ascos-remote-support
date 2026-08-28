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
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmNcMButtonDown = 0x00A7;
    private const uint WmNcMButtonUp = 0x00A8;
    private const uint WmNcRButtonDown = 0x00A4;
    private const uint WmNcRButtonUp = 0x00A5;
    private const int HtClient = 1;
    private uint _lastKeyDown;
    private bool _fallbackLogged;
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>(), 512);
    private readonly Thread _thread;
    private readonly HelperLog _log;
    private readonly CoordinateTransformationEngine _coordinates;

    public InputEngine(HelperLog log, CoordinateTransformationEngine? coordinates = null)
    {
        _log = log;
        _coordinates = coordinates ?? new CoordinateTransformationEngine();
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
                item.Completion.TrySetResult(Inject(item.Packet));
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
                        : exception.Message.StartsWith("DesktopLocked", StringComparison.Ordinal)
                            ? InputFailureStage.DesktopLocked
                            : InputFailureStage.SendInput;
                if (ShouldLogInjectionFailure())
                {
                    _log.Write("Input injection failed. Stage=" + stage + ", Win32Error=" + exception.NativeErrorCode + ". " + exception);
                    if (stage == InputFailureStage.SendInput || stage == InputFailureStage.DesktopLocked) LogForeground();
                }
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
            _log.Write("Foreground window: Process=" + processId + ", Integrity=" + ReadProcessIntegrity(processId) + ", Title='" + title + "'.");
        }
        catch (Exception exception)
        {
            _log.Write("Foreground diagnostics failed: " + exception.Message);
        }
    }

    private static string ReadProcessIntegrity(uint processId)
    {
        try
        {
            using var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
            if (process.IsInvalid) return "unknown";
            if (!OpenProcessToken(process, 0x0008, out var token)) return "unknown";
            using (token)
            {
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out var required);
                var buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, required, out _)) return "unknown";
                    var sid = new System.Security.Principal.SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                    var binary = new byte[sid.BinaryLength];
                    sid.GetBinaryForm(binary, 0);
                    var rid = BitConverter.ToInt32(binary, binary.Length - 4);
                    return "0x" + rid.ToString("X4");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch
        {
            return "unknown";
        }
    }

    private InputInjectionResult Inject(InputPacket packet)
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
                if (buttonFlag == 0) return InputInjectionResult.Failure(InputFailureStage.PacketInvalid);
                input = Mouse(point, MouseMove | buttonFlag, 0);
                break;
            case InputEventKind.Wheel:
                if (packet.Data is < -1200 or > 1200) return InputInjectionResult.Failure(InputFailureStage.PacketInvalid);
                input = Mouse(point, MouseMove | MouseWheel, unchecked((uint)packet.Data));
                break;
            case InputEventKind.Key:
                if (packet.KeyCode is 0 or > 0xFF) return InputInjectionResult.Failure(InputFailureStage.PacketInvalid);
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
            default: return InputInjectionResult.Failure(InputFailureStage.PacketInvalid);
        }

        SetLastError(0);
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
        if (sent == 1) return InputInjectionResult.Success();
        var sendInputError = Marshal.GetLastWin32Error();

        // Some environments block SendInput (UIPI/desktop restrictions) even
        // for high-integrity callers. Fall back to SetCursorPos, which is not
        // subject to UIPI inside the session, and to window messages. The
        // fallback mechanism is reported as its own stage so operators see
        // exactly how the input was delivered.
        var fallback = TryFallback(packet, point, sendInputError);
        if (fallback != 0) return InputInjectionResult.Fallback((InputFailureStage)fallback, sendInputError);

        // Only report the desktop as locked with real evidence: no foreground
        // window AND no window at the pointer position. A null foreground alone
        // is normal for an unfocused RDP session and is not a lock.
        if (GetForegroundWindow() == IntPtr.Zero && WindowFromPoint(new NativePoint(point.PixelX, point.PixelY)) == IntPtr.Zero)
            throw new Win32Exception(5, "DesktopLocked: no foreground window and no window at the pointer; the desktop appears locked or inactive.");

        throw new Win32Exception(sendInputError, "SendInput injected no events. This usually indicates UIPI or desktop-token mismatch.");
    }

    private int TryFallback(InputPacket packet, VirtualDesktopPoint point, int sendInputError)
    {
        switch (packet.Kind)
        {
            case InputEventKind.Move:
                if (SetPhysicalCursorPos(point.PixelX, point.PixelY))
                {
                    LogFallback("SetPhysicalCursorPos move, SendInputError=" + sendInputError);
                    return (int)InputFailureStage.FallbackSetCursorPos;
                }
                var physicalCursorError = Marshal.GetLastWin32Error();
                if (SetCursorPos(point.PixelX, point.PixelY))
                {
                    LogFallback("SetCursorPos move, SendInputError=" + sendInputError);
                    return (int)InputFailureStage.FallbackSetCursorPos;
                }
                var cursorError = Marshal.GetLastWin32Error();
                SetLastError(0);
                mouse_event(MouseMove | MouseAbsolute | MouseVirtualDesktop,
                    unchecked((uint)point.AbsoluteX), unchecked((uint)point.AbsoluteY), 0, UIntPtr.Zero);
                var mouseEventError = Marshal.GetLastWin32Error();
                if (mouseEventError == 0)
                {
                    LogFallback("mouse_event move (unverifiable), SendInputError=" + sendInputError);
                    return (int)InputFailureStage.FallbackMouseEvent;
                }
                if (PostToWindow(point, 0x0200 /* WM_MOUSEMOVE */, 0))
                {
                    LogFallback("PostMessage move, SendInputError=" + sendInputError);
                    return (int)InputFailureStage.FallbackPostMessage;
                }
                LogMoveChainFailure(physicalCursorError, cursorError, mouseEventError);
                return 0;
            case InputEventKind.Button:
                {
                    var message = (packet.Data, packet.Down) switch
                    {
                        (0, true) => WmLeftDown, (0, false) => WmLeftUp,
                        (1, true) => WmMiddleDown, (1, false) => WmMiddleUp,
                        (2, true) => WmRightDown, (2, false) => WmRightUp,
                        _ => 0u
                    };
                    var nonClientMessage = (packet.Data, packet.Down) switch
                    {
                        (0, true) => WmNcLButtonDown, (0, false) => WmNcLButtonUp,
                        (1, true) => WmNcMButtonDown, (1, false) => WmNcMButtonUp,
                        (2, true) => WmNcRButtonDown, (2, false) => WmNcRButtonUp,
                        _ => 0u
                    };
                    if (message == 0) return 0;

                    var mouseFlag = (packet.Data, packet.Down) switch
                    {
                        (0, true) => 0x0002u, (0, false) => 0x0004u,
                        (1, true) => 0x0020u, (1, false) => 0x0040u,
                        (2, true) => 0x0008u, (2, false) => 0x0010u,
                        _ => 0u
                    };
                    if (SetPhysicalCursorPos(point.PixelX, point.PixelY))
                    {
                        SetLastError(0);
                        mouse_event(mouseFlag, 0, 0, 0, UIntPtr.Zero);
                        if (Marshal.GetLastWin32Error() == 0)
                        {
                            LogFallback("mouse_event button (unverifiable), SendInputError=" + sendInputError);
                            return (int)InputFailureStage.FallbackMouseEvent;
                        }
                    }
                    if (PostButton(point, message, nonClientMessage))
                    {
                        LogFallback("PostMessage button, SendInputError=" + sendInputError);
                        return (int)InputFailureStage.FallbackPostMessage;
                    }
                    return 0;
                }
            case InputEventKind.Wheel:
                if (PostToWindow(point, WmMouseWheel, unchecked((uint)(packet.Data << 16))))
                {
                    LogFallback("PostMessage wheel, SendInputError=" + sendInputError);
                    return (int)InputFailureStage.FallbackPostMessage;
                }
                return 0;
            case InputEventKind.Key:
                {
                    if (packet.Down)
                    {
                        if (_lastKeyDown == packet.KeyCode) return (int)InputFailureStage.FallbackPostMessage;
                        _lastKeyDown = packet.KeyCode;
                    }
                    else if (_lastKeyDown == packet.KeyCode)
                    {
                        _lastKeyDown = 0;
                    }
                    var target = GetForegroundWindow();
                    if (target != IntPtr.Zero && PostMessage(target, packet.Down ? WmKeyDown : WmKeyUp, new IntPtr(unchecked((long)packet.KeyCode)), IntPtr.Zero))
                    {
                        LogFallback("PostMessage key, SendInputError=" + sendInputError);
                        return (int)InputFailureStage.FallbackPostMessage;
                    }
                    return 0;
                }
            default:
                return 0;
        }
    }

    private bool PostButton(VirtualDesktopPoint point, uint message, uint nonClientMessage)
    {
        var target = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
        if (target == IntPtr.Zero) return false;
        var screenLParam = new IntPtr((point.PixelY << 16) | (point.PixelX & 0xFFFF));
        var hit = SendMessage(target, WmNcHitTest, IntPtr.Zero, screenLParam);
        if (hit != IntPtr.Zero && hit.ToInt32() != HtClient && nonClientMessage != 0)
        {
            return PostMessage(target, nonClientMessage, hit, screenLParam);
        }
        var clientPoint = new NativePoint(point.PixelX, point.PixelY);
        ScreenToClient(target, ref clientPoint);
        var lParam = new IntPtr((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
        var mouseKey = message switch
        {
            WmLeftDown or WmLeftUp => 0x0001u,
            WmRightDown or WmRightUp => 0x0002u,
            WmMiddleDown or WmMiddleUp => 0x0010u,
            _ => 0u
        };
        if (message is WmLeftDown or WmMiddleDown or WmRightDown)
        {
            PostMessage(target, 0x0200 /* WM_MOUSEMOVE */, new IntPtr(mouseKey), lParam);
            PostMessage(target, 0x0021 /* WM_MOUSEACTIVATE */, new IntPtr(1), lParam);
        }
        return PostMessage(target, message, new IntPtr(mouseKey), lParam);
    }

    private bool PostToWindow(VirtualDesktopPoint point, uint message, uint wParam)
    {
        var target = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
        if (target == IntPtr.Zero) return false;
        var clientPoint = new NativePoint(point.PixelX, point.PixelY);
        ScreenToClient(target, ref clientPoint);
        var lParam = new IntPtr((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
        return PostMessage(target, message, new IntPtr(unchecked((long)wParam)), lParam);
    }

    private void LogFallback(string mechanism)
    {
        if (_fallbackLogged) return;
        _fallbackLogged = true;
        _log.Write("SendInput is blocked in this session; using the " + mechanism + " fallback.");
    }

    private int _lastMoveChainLogTick;

    private void LogMoveChainFailure(int physicalCursorError, int cursorError, int mouseEventError)
    {
        var now = Environment.TickCount;
        if (_lastMoveChainLogTick != 0 && unchecked(now - _lastMoveChainLogTick) < 2000) return;
        _lastMoveChainLogTick = now;
        _log.Write("Move fallback chain failed. SetPhysicalCursorPos=" + physicalCursorError +
            ", SetCursorPos=" + cursorError + ", mouse_event=" + mouseEventError + ".");
    }

    private int _lastInjectionLogTick;

    private bool ShouldLogInjectionFailure()
    {
        var now = Environment.TickCount;
        if (_lastInjectionLogTick != 0 && unchecked(now - _lastInjectionLogTick) < 2000) return false;
        _lastInjectionLogTick = now;
        return true;
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
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint desiredAccess, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, int informationClass, IntPtr information, int informationLength, out int returnLength);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

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
    Cancelled = 8,
    DesktopLocked = 9,
    FallbackSetCursorPos = 10,
    FallbackMouseEvent = 11,
    FallbackPostMessage = 12
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
    public static InputInjectionResult Fallback(InputFailureStage stage, int errorCode = 0) => new(true, stage, errorCode);
}
