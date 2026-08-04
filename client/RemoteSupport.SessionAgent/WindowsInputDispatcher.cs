using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteSupport.SessionAgent;

public sealed class WindowsInputDispatcher
{
    private readonly ConsentStateMachine _consent;
    private readonly Guid _sessionId;
    private readonly object _rateGate = new();
    private int _rateWindow = Environment.TickCount;
    private int _eventsInWindow;

    public WindowsInputDispatcher(ConsentStateMachine consent, Guid sessionId) => (_consent, _sessionId) = (consent, sessionId);

    public bool TryDispatch(ReadOnlySpan<byte> json)
    {
        if (!_consent.IsControlAllowed(_sessionId) || json.Length is 0 or > 4096 || !TryAcquireRatePermit()) return false;
        InputMessage? message;
        try { message = JsonSerializer.Deserialize(json, SessionAgentJsonContext.Default.InputMessage); }
        catch (JsonException) { return false; }
        if (message is null) return false;

        return message.Type switch
        {
            "move" => SendMouse(message.X, message.Y, 0x0001 | 0x8000 | 0x4000, 0),
            "button" => SendButton(message),
            "wheel" when message.Delta is >= -1200 and <= 1200 => SendMouse(message.X, message.Y, 0x0001 | 0x0800 | 0x8000 | 0x4000, message.Delta),
            "key" => SendKey(message.Code, message.Down),
            _ => false
        };
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
        return flag != 0 && SendMouse(message.X, message.Y, flag | 0x0001 | 0x8000 | 0x4000, 0);
    }

    private static bool SendMouse(int x, int y, uint flags, int data)
    {
        if (x is < 0 or > 65535 || y is < 0 or > 65535) return false;
        var input = new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { X = x, Y = y, MouseData = unchecked((uint)data), Flags = flags } } };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private static bool SendKey(string? code, bool down)
    {
        var virtualKey = MapKey(code);
        if (virtualKey == 0) return false;
        var input = new Input { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = down ? 0u : 0x0002u } } };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
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
}

internal sealed record InputMessage(string Type, int X, int Y, int Button, bool Down, int Delta, string? Code);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InputMessage))]
[JsonSerializable(typeof(RegisterDeviceRequest))]
[JsonSerializable(typeof(RegistrationResponse))]
[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(ChallengeResponse))]
[JsonSerializable(typeof(VerifyRequest))]
[JsonSerializable(typeof(AccessResponse))]
[JsonSerializable(typeof(SupportCodeResponse))]
internal partial class SessionAgentJsonContext : JsonSerializerContext;
