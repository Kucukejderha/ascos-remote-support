#pragma once

#include <windows.h>
#include "NativeShellAutomation.h"
#include <string>
#include <string_view>

struct NativeInputResult final {
    bool accepted{};
    DWORD error{};
    std::string stage;
    std::string desktop;
    std::string eventType;
    double normalizedX{-1.0};
    double normalizedY{-1.0};
    int button{-1};
};

class NativeInputEngine final {
public:
    NativeInputEngine() = default;
    ~NativeInputEngine();
    NativeInputEngine(const NativeInputEngine&) = delete;
    NativeInputEngine& operator=(const NativeInputEngine&) = delete;
    [[nodiscard]] NativeInputResult Dispatch(std::string_view json);
private:
    [[nodiscard]] bool AttachInputDesktop(NativeInputResult& result);
    [[nodiscard]] static WORD MapKey(std::string_view code);
    HDESK desktop_{};
    NativeShellAutomation shellAutomation_;
};
