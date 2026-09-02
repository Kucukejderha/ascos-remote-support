using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
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
    private const uint KeyUnicode = 0x0004;
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
    private const int HtCaption = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint WmChar = 0x0102;
    private const uint WmSysCommand = 0x0112;
    private const uint WmClose = 0x0010;
    private const uint ScMinimize = 0xF020;
    private const uint ScMaximize = 0xF030;
    private const uint ScRestore = 0xF120;
    private const uint ScClose = 0xF060;
    private const uint GaRoot = 2;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SmtoBlock = 0x0001;
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
                if (!desktop.Refresh())
                {
                    if (unchecked(Environment.TickCount - _lastLockedLogTick) > 5000)
                    {
                        _lastLockedLogTick = Environment.TickCount;
                        _log.Write("Host desktop is locked or secure (Winlogon); input is paused until it is unlocked.");
                    }
                    item.Completion.TrySetResult(InputInjectionResult.Failure(InputFailureStage.DesktopLocked, 5));
                    continue;
                }
                if (unchecked(Environment.TickCount - _lastDpiDiagTick) > 5000)
                {
                    _lastDpiDiagTick = Environment.TickCount;
                    _log.Write("DPI diag: SM_CXSCREEN=" + GetSystemMetrics(0) +
                        ", ThreadDpiAwarenessContext=0x" + GetThreadDpiAwarenessContext().ToInt64().ToString("X") + ".");
                }
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
                // SendInput absolute coordinates are mapped against the
                // caller's DPI context, which cannot be controlled reliably
                // for a SYSTEM-token helper (SetThreadDesktop resets it and
                // SetThreadDpiAwarenessContext fails afterwards). Move the
                // physical cursor directly: physical pixels, no scaling.
                if (SetPhysicalCursorPos(point.PixelX, point.PixelY)) return InputInjectionResult.Success();
                input = Mouse(point, MouseMove, 0);
                break;
            case InputEventKind.Button:
                // Title-bar system buttons (minimize/maximize/close) are
                // delivered as WM_SYSCOMMAND instead of a raw non-client click.
                // win32k can silently drop injected non-client clicks for
                // elevated windows (the RotaLink window itself), which makes
                // the _ / X buttons appear dead while other windows work.
                if (packet.Data == 0 && TryHandleSystemButton(packet, point, packet.Down))
                    return InputInjectionResult.Success();
                // Position the cursor physically first so the button event
                // lands on the exact target even if SendInput's own mapping
                // would use the logical resolution.
                SetPhysicalCursorPos(point.PixelX, point.PixelY);
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
                SetPhysicalCursorPos(point.PixelX, point.PixelY);
                input = Mouse(point, MouseMove | MouseWheel, unchecked((uint)packet.Data));
                break;
            case InputEventKind.Key:
                // Printable characters are carried in KeyCharacter and may have
                // KeyCode=0 for punctuation keys that have no virtual-key map.
                if (packet.KeyCharacter == 0 && (packet.KeyCode is 0 or > 0xFF))
                {
                    LogKeyDiagnostic(packet, "rejected");
                    return InputInjectionResult.Failure(InputFailureStage.PacketInvalid);
                }
                LogKeyDiagnostic(packet, "routed");
                if (packet.KeyCharacter != 0)
                {
                    // Unicode injection is keyboard-layout independent: the
                    // operator's browser sends the real character ('.', 'i',
                    // 'ş', ...) and the target receives exactly that character
                    // regardless of the local keyboard layout. The key release
                    // is deliberately swallowed: SendInput does not reliably
                    // match a KEYEVENTF_UNICODE key-up, and an unmatched up
                    // leaves the character stuck so later presses get eaten.
                    if (!packet.Down) return InputInjectionResult.Success();
                    input = new NativeInput
                    {
                        Type = InputKeyboard,
                        Data = new InputUnion
                        {
                            Keyboard = new KeyboardInput
                            {
                                VirtualKey = packet.KeyCharacter,
                                ScanCode = packet.KeyCharacter,
                                Flags = KeyUnicode
                            }
                        }
                    };
                    break;
                }
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
                if (_captionDragTarget != IntPtr.Zero && IsWindow(_captionDragTarget))
                {
                    // Window drag: move the caption-drag root with the cursor.
                    SetWindowPos(_captionDragTarget, IntPtr.Zero,
                        _captionWindowX + point.PixelX - _captionStartX,
                        _captionWindowY + point.PixelY - _captionStartY,
                        0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                    return (int)InputFailureStage.FallbackSetCursorPos;
                }
                var dragKeys = (_leftDown ? 0x0001u : 0u) | (_rightDown ? 0x0002u : 0u) | (_middleDown ? 0x0010u : 0u);
                if (dragKeys != 0) PostDragMove(point, dragKeys);
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

                    if (packet.Down)
                    {
                        if (packet.Data == 0) _leftDown = true;
                        else if (packet.Data == 1) _middleDown = true;
                        else if (packet.Data == 2) _rightDown = true;
                    }

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
                            if (!packet.Down) ClearButtonState();
                            return (int)InputFailureStage.FallbackMouseEvent;
                        }
                    }
                    if (PostButton(point, message, nonClientMessage))
                    {
                        LogFallback("PostMessage button, SendInputError=" + sendInputError);
                        if (!packet.Down) ClearButtonState();
                        return (int)InputFailureStage.FallbackPostMessage;
                    }
                    if (!packet.Down) ClearButtonState();
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
                    if (packet.KeyCharacter != 0)
                    {
                        // Printable character: deliver exactly one WM_CHAR with
                        // the real character from the operator; never duplicate
                        // with WM_KEYDOWN and never block repeated characters.
                        if (!packet.Down) return (int)InputFailureStage.FallbackPostMessage;
                        var charTarget = GetKeyboardTarget();
                        if (charTarget != IntPtr.Zero && PostMessage(charTarget, WmChar, new IntPtr(packet.KeyCharacter), IntPtr.Zero))
                        {
                            LogFallback("PostMessage WM_CHAR, SendInputError=" + sendInputError);
                            return (int)InputFailureStage.FallbackPostMessage;
                        }
                        return 0;
                    }
                    if (packet.Down)
                    {
                        if (_lastKeyDown == packet.KeyCode) return (int)InputFailureStage.FallbackPostMessage;
                        _lastKeyDown = packet.KeyCode;
                    }
                    else if (_lastKeyDown == packet.KeyCode)
                    {
                        _lastKeyDown = 0;
                    }
                    var target = GetKeyboardTarget();
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

    private IntPtr _lastButtonTarget;
    private int _lastButtonHitCode;
    private int _lastLeftDownTick;
    private int _lastDpiDiagTick;
    private int _lastLockedLogTick;
    private int _lastKeyDiagTick;
    private bool _systemButtonHeld;
    private long _systemButtonSequence;

    private void LogKeyDiagnostic(InputPacket packet, string disposition)
    {
        var now = Environment.TickCount;
        if (_lastKeyDiagTick != 0 && unchecked(now - _lastKeyDiagTick) < 1000) return;
        _lastKeyDiagTick = now;
        _log.Write("Key " + disposition + ". Char=0x" + packet.KeyCharacter.ToString("X4") +
            ", KeyCode=0x" + packet.KeyCode.ToString("X2") + ", Down=" + packet.Down + ".");
    }
    private IntPtr _lastLeftDownTarget;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private IntPtr _captionDragTarget;
    private int _captionStartX;
    private int _captionStartY;
    private int _captionWindowX;
    private int _captionWindowY;

    /// <summary>
    /// Delivers title-bar system-button clicks (minimize/maximize/close) as
    /// WM_SYSCOMMAND instead of relying on win32k's non-client click delivery.
    /// Injected non-client clicks can be silently dropped for elevated windows
    /// (the RotaLink window itself), which makes its _ / X buttons appear dead
    /// while every other window works. Returns true when the event was
    /// consumed. Left-button only; other buttons keep the raw SendInput path.
    /// </summary>
    /// <summary>
    /// Delivers title-bar system-button clicks (minimize/maximize/close) as
    /// WM_SYSCOMMAND instead of relying on win32k's non-client click delivery.
    /// Injected non-client clicks can be silently dropped for elevated windows
    /// (the RotaLink window itself), which makes its _ / X buttons appear dead
    /// while every other window works. Returns true when the event was
    /// consumed. Left-button only; other buttons keep the raw SendInput path.
    /// A routed down always pairs with a routed up (and a raw down never has
    /// its up consumed), so a real non-client modal tracking loop can never be
    /// left without its release event.
    /// </summary>
    private bool TryHandleSystemButton(InputPacket packet, VirtualDesktopPoint point, bool down)
    {
        if (down)
        {
            var target = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
            if (target == IntPtr.Zero) return false;
            if (!TryHitTestNonClient(target, point, out var hitCode) || hitCode is not (8 or 9 or 20))
                return false;
            _systemButtonHeld = true;
            _systemButtonSequence = packet.Sequence;
            _lastButtonTarget = target;
            _lastButtonHitCode = hitCode;
            GetWindowThreadProcessId(target, out var downPid);
            _log.Write("Title-bar system button DOWN routed. Seq=" + packet.Sequence +
                ", HWND=0x" + target.ToInt64().ToString("X") + ", PID=" + downPid + ", Hit=" + hitCode + ".");
            return true;
        }

        if (!_systemButtonHeld) return false;
        _systemButtonHeld = false;
        var recordedTarget = _lastButtonTarget;
        var recordedHit = _lastButtonHitCode;
        var recordedSequence = _systemButtonSequence;
        ClearButtonState();

        if (recordedTarget == IntPtr.Zero || !IsWindow(recordedTarget))
        {
            _log.Write("Title-bar system button target closed before release; command cancelled. Seq=" + packet.Sequence + ".");
            return true;
        }

        // Native semantics: releasing outside the button cancels the command.
        var releaseWindow = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
        var releaseHit = 0;
        if (releaseWindow != IntPtr.Zero) TryHitTestNonClient(releaseWindow, point, out releaseHit);
        if (releaseWindow == IntPtr.Zero || releaseHit != recordedHit)
        {
            _log.Write("Title-bar system button released outside the button; command cancelled. Seq=" + packet.Sequence +
                ", RecordedHit=" + recordedHit + ", ReleaseHit=" + releaseHit +
                ", ReleaseHWND=0x" + (releaseWindow == IntPtr.Zero ? 0 : releaseWindow.ToInt64()).ToString("X") + ".");
            return true;
        }

        var commandWindow = GetAncestor(recordedTarget, GaRoot);
        if (commandWindow == IntPtr.Zero) commandWindow = recordedTarget;
        var command = recordedHit switch
        {
            8 => ScMinimize,
            9 when IsZoomed(commandWindow) => ScRestore,
            9 => ScMaximize,
            20 => ScClose,
            _ => 0u
        };
        if (command == 0) return true;

        // Send the command, record the API result and error, then verify the
        // window state separately: "sent" and "applied" are distinct outcomes.
        var stopwatch = Stopwatch.StartNew();
        SetLastError(0);
        var delivered = SendMessageTimeout(commandWindow, WmSysCommand, new IntPtr(command), IntPtr.Zero,
            SmtoAbortIfHung | 0x0020 /* SMTO_ERRORONEXIT */, 500, out var commandResult);
        var sendError = Marshal.GetLastWin32Error();
        stopwatch.Stop();
        _log.Write("Title-bar system button routed. Seq=" + packet.Sequence + ", DownSeq=" + recordedSequence +
            ", Command=0x" + command.ToString("X") + ", SendMessageTimeout=" + (delivered != IntPtr.Zero) +
            ", Result=0x" + commandResult.ToInt64().ToString("X") + ", Win32Error=" + sendError +
            ", ElapsedMs=" + stopwatch.ElapsedMilliseconds + ".");

        if (command == ScClose)
        {
            Thread.Sleep(300);
            if (IsWindow(commandWindow))
            {
                PostMessage(commandWindow, WmClose, IntPtr.Zero, IntPtr.Zero);
                _log.Write("SC_CLOSE not effective; posted WM_CLOSE fallback. Seq=" + packet.Sequence + ".");
                Thread.Sleep(300);
                _log.Write(IsWindow(commandWindow)
                    ? "Close verification: window still alive. Seq=" + packet.Sequence + "."
                    : "Close verification: window closed. Seq=" + packet.Sequence + ".");
            }
            else
            {
                _log.Write("Close verification: window closed. Seq=" + packet.Sequence + ".");
            }
            return true;
        }

        Thread.Sleep(300);
        if (!IsCommandApplied(command, commandWindow))
        {
            ShowWindowAsync(commandWindow, command == ScMinimize ? 6 : command == ScMaximize ? 3 : 9);
            _log.Write("WM_SYSCOMMAND not applied; initiated ShowWindowAsync fallback and re-verifying. Seq=" + packet.Sequence + ".");
            Thread.Sleep(300);
        }
        _log.Write("Title-bar command verification: " + (IsCommandApplied(command, commandWindow) ? "applied" : "NOT applied") +
            ". Seq=" + packet.Sequence + ".");
        return true;
    }

    private static bool IsCommandApplied(uint command, IntPtr window) => command switch
    {
        ScMinimize => IsIconic(window),
        ScMaximize => IsZoomed(window),
        ScRestore => !IsIconic(window) && !IsZoomed(window),
        _ => false
    };

    private static bool TryHitTestNonClient(IntPtr target, VirtualDesktopPoint point, out int hitCode)
    {
        hitCode = 0;
        var screenLParam = new IntPtr((point.PixelY << 16) | (point.PixelX & 0xFFFF));
        var hitSuccess = SendMessageTimeout(target, WmNcHitTest, IntPtr.Zero, screenLParam,
            SmtoAbortIfHung | 0x0020 /* SMTO_ERRORONEXIT */, 200, out var hitResult);
        if (hitSuccess != IntPtr.Zero && hitResult != IntPtr.Zero) hitCode = hitResult.ToInt32();
        if (hitCode == 0) hitCode = GuessNonClientHit(target, point);
        return hitCode != 0;
    }

    private bool PostButton(VirtualDesktopPoint point, uint message, uint nonClientMessage)
    {
        var isDown = message is WmLeftDown or WmMiddleDown or WmRightDown;
        var target = IntPtr.Zero;

        // Popup menus take mouse capture in another thread's context. Resolve
        // the real menu window through the foreground thread's GUI info.
        var menuWindow = FindPopupMenuWindow(point);
        if (menuWindow != IntPtr.Zero && message is WmLeftDown or WmLeftUp)
        {
            var menuPoint = new NativePoint(point.PixelX, point.PixelY);
            ScreenToClient(menuWindow, ref menuPoint);
            var menuLParam = new IntPtr((menuPoint.Y << 16) | (menuPoint.X & 0xFFFF));
            LogFallbackOnce("PostMessage menu item");
            return PostMessage(menuWindow, message, new IntPtr(message == WmLeftDown ? 0x0001u : 0u), menuLParam);
        }

        if (!isDown && _lastButtonTarget != IntPtr.Zero && IsWindow(_lastButtonTarget))
            target = _lastButtonTarget;
        if (target == IntPtr.Zero)
        {
            target = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
            if (target == IntPtr.Zero) return false;
        }

        var screenLParam = new IntPtr((point.PixelY << 16) | (point.PixelX & 0xFFFF));
        var hitCode = 0;
        var hitSuccess = SendMessageTimeout(target, WmNcHitTest, IntPtr.Zero, screenLParam,
            SmtoAbortIfHung | SmtoBlock | 0x0020 /* SMTO_ERRORONEXIT */, 200, out var hitResult);
        if (hitSuccess != IntPtr.Zero && hitResult != IntPtr.Zero) hitCode = hitResult.ToInt32();
        if (hitCode == 0)
        {
            // The target did not answer the hit test (busy UI thread). Fall back
            // to geometry so title-bar buttons still work (e.g. RotaLink itself).
            hitCode = GuessNonClientHit(target, point);
        }

        if (hitCode != 0 && hitCode != HtClient && nonClientMessage != 0)
        {
            // Title-bar button handling: record state on down (no synthetic
            // non-client message, which can wedge modal tracking), and issue
            // the matching system command asynchronously on a matching up.
            if (isDown)
            {
                _lastButtonTarget = target;
                _lastButtonHitCode = hitCode;
                if (hitCode == HtCaption)
                {
                    var rootWindow = GetAncestor(target, GaRoot);
                    if (rootWindow != IntPtr.Zero && GetWindowRect(rootWindow, out var windowRect))
                    {
                        _captionDragTarget = rootWindow;
                        _captionWindowX = windowRect.Left;
                        _captionWindowY = windowRect.Top;
                        _captionStartX = point.PixelX;
                        _captionStartY = point.PixelY;
                    }
                }
                return true;
            }
            if (hitCode == HtCaption)
            {
                _captionDragTarget = IntPtr.Zero;
                return true;
            }
            if (_lastButtonTarget != target || _lastButtonHitCode != hitCode) return false;
            var command = hitCode switch
            {
                8 => ScMinimize,                                      // HTMINBUTTON
                9 when IsZoomed(target) => ScRestore,                 // HTMAXBUTTON
                9 => ScMaximize,
                20 => ScClose,                                        // HTCLOSE
                _ => 0u
            };
            if (command == 0) return false;
            var commandWindow = GetAncestor(target, GaRoot);
            if (commandWindow == IntPtr.Zero) commandWindow = target;
            SendMessageTimeout(commandWindow, WmSysCommand, new IntPtr(command), IntPtr.Zero,
                SmtoAbortIfHung | 0x0020 /* SMTO_ERRORONEXIT */, 500, out _);
            if (command == ScMinimize)
            {
                Thread.Sleep(100);
                if (!IsIconic(commandWindow))
                {
                    ShowWindowAsync(commandWindow, 6 /* SW_MINIMIZE */);
                    _log.Write("SC_MINIMIZE not applied via WM_SYSCOMMAND; used ShowWindowAsync fallback.");
                }
            }
            else if (command == ScMaximize)
            {
                Thread.Sleep(100);
                if (!IsZoomed(commandWindow)) ShowWindowAsync(commandWindow, 3 /* SW_MAXIMIZE */);
            }
            else if (command == ScRestore)
            {
                Thread.Sleep(100);
                if (IsIconic(commandWindow)) ShowWindowAsync(commandWindow, 9 /* SW_RESTORE */);
            }
            return true;
        }

        var clientPoint = new NativePoint(point.PixelX, point.PixelY);
        ScreenToClient(target, ref clientPoint);
        var lParam = new IntPtr((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
        // Button state at message time: pressed for downs, released for ups.
        var mouseKey = message switch
        {
            WmLeftDown => 0x0001u,
            WmRightDown => 0x0002u,
            WmMiddleDown => 0x0010u,
            _ => 0u
        };

        if (isDown)
        {
            _lastButtonTarget = target;
            _lastButtonHitCode = HtClient;
            // Activate the owning top-level window so background windows come
            // to the front on click; verify the result honestly.
            var root = GetAncestor(target, GaRoot);
            if (root != IntPtr.Zero)
            {
                if (IsIconic(root)) ShowWindowAsync(root, 9 /* SW_RESTORE */);
                if (!SetForegroundWindow(root))
                    _log.Write("Foreground activation denied for window 0x" + root.ToString("X") + ".");
            }
            if (message == WmLeftDown)
            {
                var now = Environment.TickCount;
                var doubleClickTime = GetDoubleClickTime();
                if (_lastLeftDownTick != 0 && unchecked(now - _lastLeftDownTick) >= 0 &&
                    unchecked(now - _lastLeftDownTick) < doubleClickTime && _lastLeftDownTarget == target)
                {
                    _lastLeftDownTick = 0;
                    PostMessage(target, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, lParam);
                    return PostMessage(target, 0x0203 /* WM_LBUTTONDBLCLK */, new IntPtr(mouseKey), lParam);
                }
                _lastLeftDownTick = now;
                _lastLeftDownTarget = target;
            }
            PostMessage(target, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, lParam);
            return PostMessage(target, message, new IntPtr(mouseKey), lParam);
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

    private void ClearButtonState()
    {
        _leftDown = false;
        _rightDown = false;
        _middleDown = false;
        _lastButtonTarget = IntPtr.Zero;
        _lastButtonHitCode = 0;
        _captionDragTarget = IntPtr.Zero;
        _systemButtonHeld = false;
        _systemButtonSequence = 0;
    }

    private void PostDragMove(VirtualDesktopPoint point, uint dragKeys)
    {
        var target = _lastButtonTarget;
        if (target == IntPtr.Zero || !IsWindow(target)) target = GetCapture();
        if (target == IntPtr.Zero) return;
        var clientPoint = new NativePoint(point.PixelX, point.PixelY);
        ScreenToClient(target, ref clientPoint);
        var lParam = new IntPtr((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
        PostMessage(target, 0x0200 /* WM_MOUSEMOVE */, new IntPtr(dragKeys), lParam);
    }

    /// <summary>
    /// Keyboard messages must go to the real focused child window (text boxes,
    /// custom editors), not the top-level foreground window.
    /// </summary>
    private static IntPtr GetKeyboardTarget()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return IntPtr.Zero;
        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        if (GetGUIThreadInfo(threadId, ref info) && info.Focus != IntPtr.Zero) return info.Focus;
        return foreground;
    }

    private bool _menuFallbackLogged;

    private void LogFallbackOnce(string mechanism)
    {
        if (_menuFallbackLogged) return;
        _menuFallbackLogged = true;
        _log.Write("Menu fallback active: " + mechanism + ".");
    }

    private static IntPtr FindPopupMenuWindow(VirtualDesktopPoint point)
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            if (GetGUIThreadInfo(threadId, ref info))
            {
                if (info.Capture != IntPtr.Zero && GetClassName(info.Capture) == "#32768") return info.Capture;
            }
        }
        var underPoint = WindowFromPoint(new NativePoint(point.PixelX, point.PixelY));
        if (underPoint != IntPtr.Zero && GetClassName(underPoint) == "#32768") return underPoint;
        return IntPtr.Zero;
    }

    private static int GuessNonClientHit(IntPtr target, VirtualDesktopPoint point)
    {
        if (!GetWindowRect(target, out var rect)) return HtClient;
        if (point.PixelX < rect.Left || point.PixelX > rect.Right || point.PixelY < rect.Top || point.PixelY > rect.Bottom)
            return HtClient;
        var captionHeight = GetSystemMetrics(4 /* SM_CYCAPTION */) + GetSystemMetrics(92 /* SM_CXPADDEDBORDER */);
        if (point.PixelY >= rect.Top + captionHeight) return HtClient;
        var fromRight = rect.Right - point.PixelX;
        var buttonWidth = GetSystemMetrics(49 /* SM_CXSMSIZE */);
        if (fromRight < buttonWidth) return 20;            // HTCLOSE
        if (fromRight < buttonWidth * 2) return 9;         // HTMAXBUTTON
        if (fromRight < buttonWidth * 3) return 8;         // HTMINBUTTON
        return HtCaption;
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

    public void ResetConnectionState()
    {
        _lastKeyDown = 0;
        ClearButtonState();
        _lastLeftDownTick = 0;
        _lastLeftDownTarget = IntPtr.Zero;
    }

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
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameNative(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();

    private static string GetClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        GetClassNameNative(window, builder, builder.Capacity);
        return builder.ToString();
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public WindowRect CaretRect;
    }

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
    FallbackPostMessage = 12,
    CommandTimeout = 13
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
