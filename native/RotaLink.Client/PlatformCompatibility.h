#pragma once

#include <string>

struct PlatformCompatibility final {
    bool supported{};
    bool server{};
    bool serverCore{};
    unsigned long major{};
    unsigned long minor{};
    unsigned long build{};
    std::wstring productName;
    std::wstring installationType;
    std::wstring reason;
    [[nodiscard]] std::wstring DiagnosticText() const;
    static PlatformCompatibility Evaluate();
};
