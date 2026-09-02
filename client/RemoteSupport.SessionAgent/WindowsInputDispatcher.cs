using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using RemoteSupport.Protocol;

namespace RemoteSupport.SessionAgent;

public sealed class WindowsInputDispatcher : IDisposable
{
    [ThreadStatic] private static int _lastSendInputError;
    [ThreadStatic] private static int _lastSendFailureLogTick;
    [ThreadStatic] private static string? _lastDispatchStage;
    private readonly ConsentStateMachine _consent;
    private readonly Guid _sessionId;
    private readonly object _rateGate = new();
    private int _rateWindow = Environment.TickCount;
    private int _eventsInWindow;
    private int _droppedInWindow;
    private bool _inputLogged;
    private readonly BlockingCollection<InputWorkItem> _queue = new();
    private readonly Thread _desktopThread;
    private readonly CoordinateTransformationEngine _coordinates = new();
    private readonly SessionHelperInputClient _helperInput = new();

    public WindowsInputDispatcher(ConsentStateMachine consent, Guid sessionId)
    {
        (_consent, _sessionId) = (consent, sessionId);
        _desktopThread = new Thread(DesktopThreadMain) { IsBackground = true, Name = "RotaLink input desktop" };
        _desktopThread.Start();
    }

    public bool TryDispatch(byte[] json, int length) => TryDispatchDetailed(json, length).Accepted;

    public InputDispatchReport TryDispatchDetailed(byte[] json, int length)
    {
        if (!_consent.IsControlAllowed(_sessionId)) return new InputDispatchReport(false, "consent-denied", 0, null, null);
        if (length <= 0 || length > 4096) return new InputDispatchReport(false, "packet-size-invalid", 0, null, null);
        InputMessage? message;
        try { message = new JavaScriptSerializer().Deserialize<InputMessage>(Encoding.UTF8.GetString(json, 0, length)); }
        catch (ArgumentException) { return new InputDispatchReport(false, "json-invalid", 0, null, null); }
        if (message is null) return new InputDispatchReport(false, "message-empty", 0, null, null);
        if (!TryAcquireRatePermit(message))
            return new InputDispatchReport(false, "rate-limited", 0, null, message.Type);

        if (!_inputLogged)
        {
            AppDiagnostics.Write("First remote input received. Type=" + message.Type);
            _inputLogged = true;
        }

        var helperResult = _helperInput.TrySend(message);
        if (helperResult.HasValue)
        {
            var result = helperResult.Value;
            return new InputDispatchReport(result.Accepted,
                "system-helper-" + result.Stage,
                result.ErrorCode, "elevated helper / WinSta0", message.Type);
        }
        if (InputRuntime.IsRunning != 0)
            return new InputDispatchReport(false, "system-helper-ipc-unavailable", 0, "elevated helper", message.Type);

        var work = new InputWorkItem(message);
        try { _queue.Add(work); }
        catch (InvalidOperationException) { return new InputDispatchReport(false, "input-worker-stopped", 0, null, message.Type); }
        if (!work.Completed.Wait(TimeSpan.FromSeconds(2)))
            return new InputDispatchReport(false, "input-worker-timeout", 0, null, message.Type);
        return new InputDispatchReport(work.Accepted, work.Stage, work.ErrorCode, work.Desktop, message.Type);
    }

    private void DesktopThreadMain()
    {
        using var desktop = new InputDesktopContext();
        string? lastDesktop = null;
        AppDiagnostics.Write("Dynamic input-desktop worker started.");
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                // OpenInputDesktop is deliberately called for every command. UAC,
                // unlock and Winlogon transitions can occur between two packets.
                var currentDesktop = desktop.AttachToCurrentInputDesktop();
                work.Desktop = currentDesktop;
                if (!string.Equals(lastDesktop, currentDesktop, StringComparison.Ordinal))
                {
                    AppDiagnostics.Write("Input worker attached to desktop '" + currentDesktop + "'.");
                    lastDesktop = currentDesktop;
                }
                _lastSendInputError = 0;
                _lastDispatchStage = null;
                work.Accepted = DispatchOnDesktop(work.Message);
                work.ErrorCode = _lastSendInputError;
                work.Stage = _lastDispatchStage ?? (work.Accepted ? "sendinput-ok" :
                    work.ErrorCode == 5 ? "sendinput-access-denied" :
                    work.ErrorCode == 0 ? "sendinput-blocked-by-uipi" : "sendinput-failed");
            }
            catch (Win32Exception ex)
            {
                AppDiagnostics.Write("Input desktop switch failed. Win32Error=" + ex.NativeErrorCode, ex);
                work.Accepted = false;
                work.ErrorCode = ex.NativeErrorCode;
                work.Stage = "desktop-switch-failed";
            }
            catch (InvalidOperationException ex)
            {
                AppDiagnostics.Write("Input coordinate conversion failed.", ex);
                work.Accepted = false;
                work.Stage = "coordinate-failed";
            }
            finally
            {
                work.Completed.Set();
            }
        }
    }

    private bool DispatchOnDesktop(InputMessage message) => message.Type switch
        {
            "move" => MoveCursor(ResolvePoint(message)),
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
        _helperInput.Dispose();
    }

    private bool TryAcquireRatePermit(InputMessage message)
    {
        // State-changing events (button/key, down AND up) are never dropped:
        // losing a release leaves buttons or keys stuck pressed. Only
        // continuous events (move/wheel) are rate limited.
        if (message.Type == "button" || message.Type == "key") return true;

        lock (_rateGate)
        {
            var now = Environment.TickCount;
            if (unchecked(now - _rateWindow) >= 1000)
            {
                if (_droppedInWindow > 0)
                {
                    AppDiagnostics.Write("Rate limit: " + _droppedInWindow + " input events were dropped in the last second.");
                    _droppedInWindow = 0;
                }
                _rateWindow = now;
                _eventsInWindow = 0;
            }
            if (_eventsInWindow < 240)
            {
                _eventsInWindow++;
                return true;
            }
            _droppedInWindow++;
            return false;
        }
    }

    private bool SendButton(InputMessage message)
    {
        var flag = (message.Button, message.Down) switch
        {
            (0, true) => 0x0002u, (0, false) => 0x0004u,
            (1, true) => 0x0020u, (1, false) => 0x0040u,
            (2, true) => 0x0008u, (2, false) => 0x0010u,
            _ => 0u
        };
        if (flag == 0) return false;
        var point = ResolvePoint(message);
        return SendInputs(MouseMoveInput(point), new Input
        {
            Type = 0,
            Union = new InputUnion { Mouse = new MouseInput { Flags = flag } }
        });
    }

    private bool SendWheel(InputMessage message)
    {
        var point = ResolvePoint(message);
        return SendInputs(MouseMoveInput(point), new Input
        {
            Type = 0,
            Union = new InputUnion { Mouse = new MouseInput { MouseData = unchecked((uint)message.Delta), Flags = 0x0800u } }
        });
    }

    private VirtualDesktopPoint ResolvePoint(InputMessage message)
    {
        var normalizedX = message.NormalizedX ?? message.X / 65535d;
        var normalizedY = message.NormalizedY ?? message.Y / 65535d;
        return _coordinates.Transform(normalizedX, normalizedY);
    }

    private static bool MoveCursor(VirtualDesktopPoint point) => SendInputs(MouseMoveInput(point));

    private static Input MouseMoveInput(VirtualDesktopPoint point)
    {
        return new Input
        {
            Type = 0,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    X = point.AbsoluteX,
                    Y = point.AbsoluteY,
                    Flags = 0x8000u | 0x4000u | 0x0001u
                }
            }
        };
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

    private static bool IsExtendedKey(ushort key) => key is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x6F or 0x90 or 0x91;

    private static bool SendInputs(params Input[] inputs)
    {
        SetLastError(0);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent == inputs.Length) return true;
        _lastSendInputError = Marshal.GetLastWin32Error();
        LogInputFailure("SendInput", sent, inputs.Length, _lastSendInputError);
        return false;
    }

    private static void LogInputFailure(string api, long sent, int expected, int error)
    {
        var now = Environment.TickCount;
        if (_lastSendFailureLogTick != 0 && unchecked(now - _lastSendFailureLogTick) < 2000) return;
        _lastSendFailureLogTick = now;
        AppDiagnostics.Write(api + " failed. Sent=" + sent + "/" + expected + ", Win32Error=" + error);
    }

    internal static ushort MapKey(string? code)
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
            // Punctuation and symbol keys keep working when the browser does
            // not provide a single-character e.key for them (dead keys, some
            // IME states); the values are the US VK_OEM_* virtual-key codes.
            "Period" => 0xBE, "Comma" => 0xBC, "Semicolon" => 0xBA, "Quote" => 0xDE,
            "Backquote" => 0xC0, "BracketLeft" => 0xDB, "BracketRight" => 0xDD,
            "Slash" => 0xBF, "Backslash" or "IntlBackslash" => 0xDC, "Minus" => 0xBD, "Equal" => 0xBB,
            "IntlRo" => 0xC0, "IntlYen" => 0xDC,
            _ => 0
        };
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint errorCode);

    private sealed class InputWorkItem
    {
        public InputWorkItem(InputMessage message) => Message = message;
        public InputMessage Message { get; }
        public ManualResetEventSlim Completed { get; } = new(false);
        public bool Accepted { get; set; }
        public string Stage { get; set; } = "not-dispatched";
        public int ErrorCode { get; set; }
        public string? Desktop { get; set; }
    }
}

public sealed class InputDispatchReport
{
    public InputDispatchReport(bool accepted, string stage, int errorCode, string? desktop, string? eventType)
    {
        Accepted = accepted; Stage = stage; ErrorCode = errorCode; Desktop = desktop; EventType = eventType;
    }
    public bool Accepted { get; }
    public string Stage { get; }
    public int ErrorCode { get; }
    public string? Desktop { get; }
    public string? EventType { get; }
}

internal sealed class InputMessage
{
    public string? Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public double? NormalizedX { get; set; }
    public double? NormalizedY { get; set; }
    public int Button { get; set; }
    public bool Down { get; set; }
    public int Delta { get; set; }
    public string? Code { get; set; }
    public string? Key { get; set; }
}
