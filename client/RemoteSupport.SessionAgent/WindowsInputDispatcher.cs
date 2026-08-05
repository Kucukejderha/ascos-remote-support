using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace RemoteSupport.SessionAgent;

public sealed class WindowsInputDispatcher : IDisposable
{
    private readonly ConsentStateMachine _consent;
    private readonly Guid _sessionId;
    private readonly object _rateGate = new();
    private int _rateWindow = Environment.TickCount;
    private int _eventsInWindow;
    private bool _inputLogged;
    private readonly BlockingCollection<InputWorkItem> _queue = new();
    private readonly Thread _desktopThread;

    public WindowsInputDispatcher(ConsentStateMachine consent, Guid sessionId)
    {
        (_consent, _sessionId) = (consent, sessionId);
        _desktopThread = new Thread(DesktopThreadMain) { IsBackground = true, Name = "RotaLink input desktop" };
        _desktopThread.Start();
    }

    public bool TryDispatch(byte[] json, int length)
    {
        if (!_consent.IsControlAllowed(_sessionId) || length <= 0 || length > 4096 || !TryAcquireRatePermit()) return false;
        InputMessage? message;
        try { message = new JavaScriptSerializer().Deserialize<InputMessage>(Encoding.UTF8.GetString(json, 0, length)); }
        catch (ArgumentException) { return false; }
        if (message is null) return false;

        if (!_inputLogged)
        {
            AppDiagnostics.Write("First remote input received. Type=" + message.Type);
            _inputLogged = true;
        }

        var work = new InputWorkItem(message);
        try { _queue.Add(work); }
        catch (InvalidOperationException) { return false; }
        return work.Completed.Wait(TimeSpan.FromSeconds(2)) && work.Accepted;
    }

    private void DesktopThreadMain()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001u | 0x0080u | 0x0100u);
        if (desktop == IntPtr.Zero || !SetThreadDesktop(desktop))
        {
            AppDiagnostics.Write("Could not attach input worker to the active desktop. Win32Error=" + Marshal.GetLastWin32Error());
            foreach (var rejected in _queue.GetConsumingEnumerable()) rejected.Completed.Set();
            if (desktop != IntPtr.Zero) CloseDesktop(desktop);
            return;
        }

        AppDiagnostics.Write("Input worker attached to the active Windows desktop.");
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work.Accepted = DispatchOnDesktop(work.Message);
            work.Completed.Set();
        }
        CloseDesktop(desktop);
    }

    private static bool DispatchOnDesktop(InputMessage message) => message.Type switch
        {
            "move" => MoveCursor(message.X, message.Y),
            "button" => SendButton(message),
            "wheel" when message.Delta is >= -1200 and <= 1200 => SendWheel(message),
            "key" => SendKey(message.Code, message.Down),
            _ => false
        };

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_desktopThread.Join(TimeSpan.FromSeconds(2))) AppDiagnostics.Write("Input desktop worker did not stop in time.");
        _queue.Dispose();
    }

    private bool TryAcquireRatePermit()
    {
        lock (_rateGate)
        {
            var now = Environment.TickCount;
            if (unchecked(now - _rateWindow) >= 1000) { _rateWindow = now; _eventsInWindow = 0; }
            return ++_eventsInWindow <= 240;
        }
    }

    private static bool SendButton(InputMessage message)
    {
        var flag = (message.Button, message.Down) switch
        {
            (0, true) => 0x0002u, (0, false) => 0x0004u,
            (1, true) => 0x0020u, (1, false) => 0x0040u,
            (2, true) => 0x0008u, (2, false) => 0x0010u,
            _ => 0u
        };
        return flag != 0 && SendMouse(message.X, message.Y, flag, 0);
    }

    private static bool SendWheel(InputMessage message)
    {
        return SendMouse(message.X, message.Y, 0x0800u, unchecked((uint)message.Delta));
    }

    private static bool MoveCursor(int x, int y)
    {
        if (x is < 0 or > 65535 || y is < 0 or > 65535) return false;
        return SendInputs(new Input
        {
            Type = 0,
            Union = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Flags = 0x8000u | 0x4000u | 0x0001u } }
        });
    }

    private static bool SendMouse(int x, int y, uint action, uint data)
    {
        if (x is < 0 or > 65535 || y is < 0 or > 65535) return false;
        return SendInputs(
            new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Flags = 0x8000u | 0x4000u | 0x0001u } } },
            new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { MouseData = data, Flags = action } } });
    }

    private static bool SendKey(string? code, bool down)
    {
        var virtualKey = MapKey(code);
        if (virtualKey == 0) return false;
        return SendInputs(new Input
        {
            Type = 1,
            Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = down ? 0u : 0x0002u } }
        });
    }

    private static bool SendInputs(params Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent == inputs.Length) return true;
        AppDiagnostics.Write("SendInput failed. Sent=" + sent + "/" + inputs.Length + ", Win32Error=" + Marshal.GetLastWin32Error());
        return false;
    }

    private static ushort MapKey(string? code)
    {
        if (code is { Length: 4 } && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z') return code[3];
        if (code is { Length: 6 } && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9') return code[5];
        return code switch
        {
            "Enter" => 0x0D, "Escape" => 0x1B, "Backspace" => 0x08, "Tab" => 0x09, "Space" => 0x20,
            "Delete" => 0x2E, "Insert" => 0x2D, "Home" => 0x24, "End" => 0x23, "PageUp" => 0x21, "PageDown" => 0x22,
            "ArrowLeft" => 0x25, "ArrowUp" => 0x26, "ArrowRight" => 0x27, "ArrowDown" => 0x28,
            "ShiftLeft" or "ShiftRight" => 0x10, "ControlLeft" or "ControlRight" => 0x11, "AltLeft" or "AltRight" => 0x12,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73, "F5" => 0x74, "F6" => 0x75,
            "F7" => 0x76, "F8" => 0x77, "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => 0
        };
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);

    private sealed class InputWorkItem
    {
        public InputWorkItem(InputMessage message) => Message = message;
        public InputMessage Message { get; }
        public ManualResetEventSlim Completed { get; } = new(false);
        public bool Accepted { get; set; }
    }
}

internal sealed class InputMessage
{
    public string? Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Button { get; set; }
    public bool Down { get; set; }
    public int Delta { get; set; }
    public string? Code { get; set; }
}
