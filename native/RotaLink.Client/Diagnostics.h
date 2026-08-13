#pragma once

#include <string>

class Diagnostics final {
public:
    static void Initialize();
    static void Write(const std::wstring& message) noexcept;
    [[nodiscard]] static std::wstring LogPath();
};
