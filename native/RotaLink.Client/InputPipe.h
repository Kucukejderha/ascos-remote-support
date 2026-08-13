#pragma once

#include <windows.h>
#include <cstdint>
#include <string>
#include <string_view>

class InputPipeClient final {
public:
    explicit InputPipeClient(DWORD clientProcessId) noexcept : clientProcessId_(clientProcessId) {}
    ~InputPipeClient();
    InputPipeClient(const InputPipeClient&) = delete;
    InputPipeClient& operator=(const InputPipeClient&) = delete;
    [[nodiscard]] bool TryDispatch(std::string_view requestJson, std::string& responseJson) noexcept;
private:
    [[nodiscard]] bool Connect() noexcept;
    void Close() noexcept;
    DWORD clientProcessId_{};
    HANDLE pipe_{INVALID_HANDLE_VALUE};
};

class InputPipeServer final {
public:
    explicit InputPipeServer(DWORD allowedClientProcessId) : allowedClientProcessId_(allowedClientProcessId) {}
    InputPipeServer(const InputPipeServer&) = delete;
    InputPipeServer& operator=(const InputPipeServer&) = delete;
    [[nodiscard]] int Run();
private:
    DWORD allowedClientProcessId_{};
};
