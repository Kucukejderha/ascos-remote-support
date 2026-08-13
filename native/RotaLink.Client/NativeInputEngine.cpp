#include "NativeInputEngine.h"
#include "JsonLite.h"
#include <algorithm>
#include <cmath>
#include <limits>
#include <vector>

namespace {
constexpr DWORD MouseMove = 0x0001;
constexpr DWORD MouseLeftDown = 0x0002;
constexpr DWORD MouseLeftUp = 0x0004;
constexpr DWORD MouseRightDown = 0x0008;
constexpr DWORD MouseRightUp = 0x0010;
constexpr DWORD MouseMiddleDown = 0x0020;
constexpr DWORD MouseMiddleUp = 0x0040;
constexpr DWORD MouseWheel = 0x0800;
constexpr DWORD MouseVirtualDesktop = 0x4000;
constexpr DWORD MouseAbsolute = 0x8000;

INPUT Mouse(double normalizedX, double normalizedY, DWORD flags, DWORD data = 0) {
    const double x = std::clamp(normalizedX, 0.0, 1.0);
    const double y = std::clamp(normalizedY, 0.0, 1.0);
    INPUT input{};
    input.type = INPUT_MOUSE;
    input.mi.dx = static_cast<LONG>(std::llround(x * 65535.0));
    input.mi.dy = static_cast<LONG>(std::llround(y * 65535.0));
    input.mi.mouseData = data;
    input.mi.dwFlags = MouseAbsolute | MouseVirtualDesktop | flags;
    return input;
}

bool Send(std::vector<INPUT>& inputs, NativeInputResult& result) {
    if (inputs.empty() || inputs.size() > (std::numeric_limits<UINT>::max)()) {
        result.stage = "input-count-invalid";
        return false;
    }
    const UINT expected = static_cast<UINT>(inputs.size());
    SetLastError(ERROR_SUCCESS);
    const UINT sent = SendInput(expected, inputs.data(), sizeof(INPUT));
    if (sent == expected) return true;
    result.error = GetLastError();
    result.stage = result.error == ERROR_ACCESS_DENIED ? "sendinput-access-denied" : "sendinput-failed";
    return false;
}

bool IsNormalized(double value) { return std::isfinite(value) && value >= 0.0 && value <= 1.0; }
}

NativeInputEngine::~NativeInputEngine() {
    // Windows owns the desktop assigned to this worker thread until the thread
    // exits. Closing an assigned desktop handle is forbidden; process teardown
    // closes it after the input thread has ended.
}

bool NativeInputEngine::AttachInputDesktop(NativeInputResult& result) {
    constexpr ACCESS_MASK access = DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS | DESKTOP_SWITCHDESKTOP;
    HDESK next = OpenInputDesktop(0, FALSE, access);
    if (!next) {
        result.error = GetLastError(); result.stage = "open-input-desktop-failed"; return false;
    }
    if (next != desktop_) {
        if (!SetThreadDesktop(next)) {
            result.error = GetLastError(); result.stage = "set-thread-desktop-failed"; CloseDesktop(next); return false;
        }
        HDESK previous = desktop_;
        desktop_ = next;
        if (previous && !CloseDesktop(previous)) {
            result.error = GetLastError(); result.stage = "close-previous-desktop-failed"; return false;
        }
    }
    wchar_t name[256]{};
    DWORD needed = 0;
    result.desktop = GetUserObjectInformationW(desktop_, UOI_NAME, name, sizeof(name), &needed)
        ? JsonLite::Utf8(name) : "input-desktop";
    return true;
}

NativeInputResult NativeInputEngine::Dispatch(std::string_view json) {
    NativeInputResult result;
    try {
        const std::string type = JsonLite::StringValue(json, "type");
        result.eventType = type;
        if (!AttachInputDesktop(result)) return result;
        const double x = JsonLite::NumberValue(json, "normalizedX", -1.0);
        const double y = JsonLite::NumberValue(json, "normalizedY", -1.0);
        std::vector<INPUT> inputs;
        if (type == "key") {
            const WORD key = MapKey(JsonLite::StringValue(json, "code"));
            if (key == 0) { result.stage = "key-invalid"; return result; }
            INPUT input{};
            input.type = INPUT_KEYBOARD;
            input.ki.wVk = key;
            const bool extended = key == VK_PRIOR || key == VK_NEXT || key == VK_END || key == VK_HOME ||
                key == VK_LEFT || key == VK_UP || key == VK_RIGHT || key == VK_DOWN || key == VK_INSERT ||
                key == VK_DELETE || key == VK_DIVIDE || key == VK_NUMLOCK;
            input.ki.dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) |
                (JsonLite::BooleanValue(json, "down", false) ? 0 : KEYEVENTF_KEYUP);
            inputs.push_back(input);
        } else {
            if (!IsNormalized(x) || !IsNormalized(y)) { result.stage = "coordinate-invalid"; return result; }
            if (type == "move") inputs.push_back(Mouse(x, y, MouseMove));
            else if (type == "wheel") {
                const int delta = static_cast<int>(JsonLite::NumberValue(json, "delta", 0));
                if (delta < -1200 || delta > 1200) { result.stage = "wheel-invalid"; return result; }
                inputs.push_back(Mouse(x, y, MouseMove | MouseWheel, static_cast<DWORD>(delta)));
            } else if (type == "button" || type == "click") {
                const int button = static_cast<int>(JsonLite::NumberValue(json, "button", -1));
                DWORD down = 0, up = 0;
                if (button == 0) { down = MouseLeftDown; up = MouseLeftUp; }
                else if (button == 1) { down = MouseMiddleDown; up = MouseMiddleUp; }
                else if (button == 2) { down = MouseRightDown; up = MouseRightUp; }
                else { result.stage = "button-invalid"; return result; }
                inputs.push_back(Mouse(x, y, MouseMove));
                if (type == "click") {
                    inputs.push_back(Mouse(x, y, down)); inputs.push_back(Mouse(x, y, up));
                } else inputs.push_back(Mouse(x, y,
                    JsonLite::BooleanValue(json, "down", false) ? down : up));
            } else { result.stage = "event-invalid"; return result; }
        }
        result.accepted = Send(inputs, result);
        if (result.accepted) result.stage = "native-sendinput-ok";
        return result;
    } catch (...) {
        result.stage = "json-invalid";
        return result;
    }
}

WORD NativeInputEngine::MapKey(std::string_view code) {
    if (code.size() == 4 && code.starts_with("Key") && code[3] >= 'A' && code[3] <= 'Z') return code[3];
    if (code.size() == 6 && code.starts_with("Digit") && code[5] >= '0' && code[5] <= '9') return code[5];
    struct Pair { std::string_view code; WORD key; };
    constexpr Pair values[] = {
        {"Enter",VK_RETURN},{"Escape",VK_ESCAPE},{"Backspace",VK_BACK},{"Tab",VK_TAB},{"Space",VK_SPACE},
        {"Delete",VK_DELETE},{"Insert",VK_INSERT},{"Home",VK_HOME},{"End",VK_END},{"PageUp",VK_PRIOR},
        {"PageDown",VK_NEXT},{"ArrowLeft",VK_LEFT},{"ArrowUp",VK_UP},{"ArrowRight",VK_RIGHT},{"ArrowDown",VK_DOWN},
        {"ShiftLeft",VK_SHIFT},{"ShiftRight",VK_SHIFT},{"ControlLeft",VK_CONTROL},{"ControlRight",VK_CONTROL},
        {"AltLeft",VK_MENU},{"AltRight",VK_MENU},{"MetaLeft",VK_LWIN},{"MetaRight",VK_RWIN},{"ContextMenu",VK_APPS},
        {"CapsLock",VK_CAPITAL},{"NumLock",VK_NUMLOCK},{"ScrollLock",VK_SCROLL},{"Pause",VK_PAUSE},{"PrintScreen",VK_SNAPSHOT},
        {"F1",VK_F1},{"F2",VK_F2},{"F3",VK_F3},{"F4",VK_F4},{"F5",VK_F5},{"F6",VK_F6},
        {"F7",VK_F7},{"F8",VK_F8},{"F9",VK_F9},{"F10",VK_F10},{"F11",VK_F11},{"F12",VK_F12}
    };
    for (const auto& value : values) if (value.code == code) return value.key;
    return 0;
}
