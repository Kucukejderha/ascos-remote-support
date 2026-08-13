#pragma once

#include <windows.h>
#include <filesystem>
#include <memory>

class NativeRuntime final {
public:
    NativeRuntime() = default;
    ~NativeRuntime();
    NativeRuntime(const NativeRuntime&) = delete;
    NativeRuntime& operator=(const NativeRuntime&) = delete;
    void StartForCurrentClient();
    void Stop() noexcept;
    [[nodiscard]] static int RunServiceMode();
    [[nodiscard]] static int RunHelperMode(DWORD allowedClientProcessId);
private:
    SC_HANDLE manager_{};
    SC_HANDLE service_{};
    std::filesystem::path installedRuntime_;
};
