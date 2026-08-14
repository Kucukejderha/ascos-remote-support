#pragma once

#include <windows.h>
#include <string>

enum class ShellAutomationStatus {
    NotShell,
    Handled,
    Failed
};

struct ShellAutomationResult final {
    ShellAutomationStatus status{ShellAutomationStatus::NotShell};
    DWORD error{};
    std::string stage;
    std::wstring targetClass;
    std::wstring targetName;
};

class NativeShellAutomation final {
public:
    [[nodiscard]] ShellAutomationResult TryHandleClick(POINT point, unsigned button) noexcept;
private:
    DWORD lastDesktopClickTick_{};
    POINT lastDesktopPoint_{};
    HWND lastDesktopWindow_{};
};
