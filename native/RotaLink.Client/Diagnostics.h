#pragma once

#include <string>

class Diagnostics final {
public:
    [[nodiscard]] static bool Initialize(const std::wstring& directory = {}) noexcept;
    static void Write(const std::wstring& message) noexcept;
    [[nodiscard]] static std::wstring LogPath();
};
