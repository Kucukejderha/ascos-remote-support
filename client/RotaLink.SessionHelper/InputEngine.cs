using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>(), 512);
    private readonly Thread _thread;
    private readonly HelperLog _log;
    private long _lastSequence;

    public InputEngine(HelperLog log)
    {
        _log = log;
        _thread = new Thread(Worker) { IsBackground = true, Name = "RotaLink secure input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public Task<bool> InjectAsync(InputPacket packet, CancellationToken cancellationToken)
    {
        if (packet.Sequence <= Interlocked.Read(ref _lastSequence)) return Task.FromResult(false);
        var item = new WorkItem(packet, cancellationToken);
        if (!_queue.TryAdd(item)) return Task.FromResult(false);
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
                if (accepted) Interlocked.Exchange(ref _lastSequence, item.Packet.Sequence);
                item.Completion.TrySetResult(accepted);
            }
            catch (OperationCanceledException) { item.Completion.TrySetCanceled(item.CancellationToken); }
            catch (Exception exception)
            {
                _log.Write("Input injection failed: " + exception);
                item.Completion.TrySetResult(false);
            }
        }
    }

    private static bool Inject(InputPacket packet)
    {
        var point = new CoordinateTransformationEngine().Transform(packet.NormalizedX, packet.NormalizedY);
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

        Marshal.SetLastPInvokeError(0);
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
        if (sent == 1) return true;
        throw new Win32Exception(Marshal.GetLastPInvokeError(), "SendInput injected no events. This usually indicates UIPI or desktop-token mismatch.");
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

    private sealed record WorkItem(InputPacket Packet, CancellationToken CancellationToken)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
}
