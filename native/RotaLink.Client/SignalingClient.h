#pragma once

#include "CngDeviceIdentity.h"
#include <windows.h>
#include <winhttp.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <span>
#include <string>
#include <vector>

struct NativeHostSession final {
    std::string deviceId;
    std::string sessionId;
    std::string code;
    std::string accessToken;
};

class NativeWebSocket final {
public:
    NativeWebSocket() noexcept = default;
    explicit NativeWebSocket(HINTERNET handle) noexcept : handle_(handle) {}
    ~NativeWebSocket();
    NativeWebSocket(const NativeWebSocket&) = delete;
    NativeWebSocket& operator=(const NativeWebSocket&) = delete;
    NativeWebSocket(NativeWebSocket&& other) noexcept;
    NativeWebSocket& operator=(NativeWebSocket&& other) noexcept;
    [[nodiscard]] bool IsOpen() const noexcept;
    [[nodiscard]] bool Receive(std::vector<std::uint8_t>& message, bool& binary, std::atomic_bool& stopping);
    void SendText(std::string_view message);
    void SendBinary(std::span<const std::uint8_t> message);
    void Shutdown() noexcept;
private:
    std::atomic<HINTERNET> handle_{};
    std::mutex sendMutex_;
};

class SignalingClient final {
public:
    SignalingClient();
    ~SignalingClient();
    SignalingClient(const SignalingClient&) = delete;
    SignalingClient& operator=(const SignalingClient&) = delete;
    [[nodiscard]] NativeHostSession CreateSession();
    [[nodiscard]] NativeWebSocket ConnectHostSocket(const NativeHostSession& session, std::string_view channel);
private:
    [[nodiscard]] std::string Post(std::wstring_view path, std::string_view body,
        std::string_view bearerToken = {});
    CngDeviceIdentity identity_;
    HINTERNET session_{};
    HINTERNET connection_{};
};
