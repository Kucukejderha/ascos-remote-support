using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

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
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>(), 512);
    private readonly Thread _thread;
    private readonly HelperLog _log;
    private readonly ForegroundActivation _foreground;

    public InputEngine(HelperLog log)
    {
        _log = log;
        _foreground = new ForegroundActivation(log);
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
                item.Completion.TrySetResult(InputInjectionResult.Failure(stage, exception.NativeErrorCode));
            }
            catch (Exception exception)
            {
                _log.Write("Input injection failed: " + exception);
                item.Completion.TrySetResult(InputInjectionResult.Failure(InputFailureStage.HelperException, exception.HResult));
            }
        }
    }

    private bool Inject(InputPacket packet)
    {
        var point = new CoordinateTransformationEngine().Transform(packet.NormalizedX, packet.NormalizedY);
        switch (packet.Kind)
        {
            case InputEventKind.Move:
                return SendInputs(Mouse(point, MouseMove, 0));
            case InputEventKind.Button:
                var buttonFlag = (packet.Data, packet.Down) switch
                {
                    (0, true) => MouseLeftDown, (0, false) => MouseLeftUp,
                    (1, true) => MouseMiddleDown, (1, false) => MouseMiddleUp,
                    (2, true) => MouseRightDown, (2, false) => MouseRightUp,
                    _ => 0u
                };
                if (buttonFlag == 0) return false;
                using (packet.Down ? _foreground.PrepareForClick(point) : null)
                    return SendInputs(Mouse(point, MouseMove | buttonFlag, 0));
            case InputEventKind.Click:
                var clickFlags = packet.Data switch
                {
                    0 => (Down: MouseLeftDown, Up: MouseLeftUp),
                    1 => (Down: MouseMiddleDown, Up: MouseMiddleUp),
                    2 => (Down: MouseRightDown, Up: MouseRightUp),
                    _ => (Down: 0u, Up: 0u)
                };
                if (clickFlags.Down == 0) return false;
                using (_foreground.PrepareForClick(point))
                    return SendInputs(
                        Mouse(point, MouseMove, 0),
                        Mouse(point, clickFlags.Down, 0),
                        Mouse(point, clickFlags.Up, 0));
            case InputEventKind.Wheel:
                if (packet.Data is < -1200 or > 1200) return false;
                return SendInputs(Mouse(point, MouseMove | MouseWheel, unchecked((uint)packet.Data)));
            case InputEventKind.Key:
                if (packet.KeyCode is 0 or > 0xFF) return false;
                var extended = IsExtendedKey(packet.KeyCode) ? KeyExtended : 0u;
                return SendInputs(new NativeInput
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
                });
            default: return false;
        }
    }

    private static bool SendInputs(params NativeInput[] inputs)
    {
        SetLastError(0);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent == inputs.Length) return true;
        throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput injected no events. This usually indicates UIPI or desktop-token mismatch.");
    }

    private static NativeInput Mouse(AbsolutePoint point, uint flags, uint data) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput
            {
                X = point.X,
                Y = point.Y,
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

internal enum InputEventKind : byte { Move = 1, Button = 2, Wheel = 3, Key = 4, Click = 5 }

internal sealed class InputPacket
{
    public InputPacket(InputEventKind kind, bool down, long sequence, double normalizedX, double normalizedY, int data, uint keyCode)
    {
        Kind = kind;
        Down = down;
        Sequence = sequence;
        NormalizedX = normalizedX;
        NormalizedY = normalizedY;
        Data = data;
        KeyCode = keyCode;
    }

    public InputEventKind Kind { get; }
    public bool Down { get; }
    public long Sequence { get; }
    public double NormalizedX { get; }
    public double NormalizedY { get; }
    public int Data { get; }
    public uint KeyCode { get; }
}
