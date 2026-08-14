#pragma once

#include "SignalingClient.h"
#include <atomic>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

class SessionRuntime final {
public:
    using ReadyCallback = std::function<void(const NativeHostSession&)>;
    using StatusCallback = std::function<void(std::wstring, bool)>;
    SessionRuntime(ReadyCallback ready, StatusCallback status);
    ~SessionRuntime();
    SessionRuntime(const SessionRuntime&) = delete;
    SessionRuntime& operator=(const SessionRuntime&) = delete;
    void Start();
    void Stop() noexcept;
private:
    void Run() noexcept;
    void ControlLoop(NativeWebSocket& socket);
    void VideoLoop(NativeWebSocket& socket);
    void DxgiVideoLoop(NativeWebSocket& socket);
    void GdiVideoLoop(NativeWebSocket& socket);
    ReadyCallback ready_;
    StatusCallback status_;
    std::atomic_bool stopping_{};
    std::thread worker_;
    std::thread videoWorker_;
    std::mutex socketsMutex_;
    NativeWebSocket* control_{};
    NativeWebSocket* video_{};
};
