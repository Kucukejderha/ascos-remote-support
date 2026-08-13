#include "SessionRuntime.h"
#include "Diagnostics.h"
#include "DesktopDuplicator.h"
#include "GpuColorConverter.h"
#include "H264Encoder.h"
#include "GdiJpegCapture.h"
#include "JsonLite.h"
#include "NativeInputEngine.h"
#include <windows.h>
#include <objbase.h>
#include <chrono>
#include <cstring>
#include <exception>
#include <stdexcept>
#include <utility>

SessionRuntime::SessionRuntime(ReadyCallback ready, StatusCallback status)
    : ready_(std::move(ready)), status_(std::move(status)) {}

SessionRuntime::~SessionRuntime() { Stop(); }

void SessionRuntime::Start() {
    if (worker_.joinable()) return;
    stopping_.store(false);
    worker_ = std::thread([this] { Run(); });
}

void SessionRuntime::Stop() noexcept {
    stopping_.store(true);
    {
        std::scoped_lock lock(socketsMutex_);
        if (control_) control_->Shutdown();
        if (video_) video_->Shutdown();
    }
    if (worker_.joinable() && worker_.get_id() != std::this_thread::get_id()) worker_.join();
    if (videoWorker_.joinable() && videoWorker_.get_id() != std::this_thread::get_id()) videoWorker_.join();
}

void SessionRuntime::Run() noexcept {
    try {
        status_(L"Cihaz kimliği doğrulanıyor…", false);
        SignalingClient signaling;
        NativeHostSession session = signaling.CreateSession();
        if (stopping_.load()) return;
        Diagnostics::Write(L"Native support session prepared. DeviceId=" + std::wstring(session.deviceId.begin(), session.deviceId.end()) +
            L", SessionId=" + std::wstring(session.sessionId.begin(), session.sessionId.end()));
        ready_(session);
        status_(L"Kontrol ve görüntü kanalları bağlanıyor…", false);
        NativeWebSocket control = signaling.ConnectHostSocket(session, "control");
        NativeWebSocket video = signaling.ConnectHostSocket(session, "video");
        {
            std::scoped_lock lock(socketsMutex_);
            control_ = &control;
            video_ = &video;
        }
        try {
            Diagnostics::Write(L"Native control and video WebSockets connected independently.");
            status_(L"Bağlantı aktif — native görüntü ve kontrol hazır.", false);
            videoWorker_ = std::thread([this, &video] {
                try { VideoLoop(video); }
                catch (const std::exception& error) {
                    const std::string narrow(error.what());
                    Diagnostics::Write(L"Native video loop failed: " + std::wstring(narrow.begin(), narrow.end()));
                    stopping_.store(true);
                    std::scoped_lock lock(socketsMutex_);
                    if (control_) control_->Shutdown();
                }
            });
            ControlLoop(control);
        } catch (...) {
            stopping_.store(true);
            control.Shutdown();
            video.Shutdown();
            if (videoWorker_.joinable()) videoWorker_.join();
            {
                std::scoped_lock lock(socketsMutex_);
                control_ = nullptr;
                video_ = nullptr;
            }
            throw;
        }
        const bool locallyStopped = stopping_.exchange(true);
        control.Shutdown();
        video.Shutdown();
        if (videoWorker_.joinable()) videoWorker_.join();
        {
            std::scoped_lock lock(socketsMutex_);
            control_ = nullptr;
            video_ = nullptr;
        }
        if (!locallyStopped) status_(L"Bağlantı kapandı; uygulamayı yeniden açın.", true);
    } catch (const std::exception& error) {
        const std::string narrowMessage(error.what());
        const std::wstring message(narrowMessage.begin(), narrowMessage.end());
        Diagnostics::Write(L"Native session failed: " + message);
        if (!stopping_.load()) status_(L"Bağlantı kurulamadı: " + message, true);
    } catch (...) {
        Diagnostics::Write(L"Native session failed with an unknown error.");
        if (!stopping_.load()) status_(L"Bağlantı kurulamadı: bilinmeyen hata", true);
    }
}

void SessionRuntime::ControlLoop(NativeWebSocket& socket) {
    NativeInputEngine input;
    std::vector<std::uint8_t> message;
    bool binary = false;
    while (!stopping_.load() && socket.Receive(message, binary, stopping_)) {
        if (binary) continue;
        const std::string json(message.begin(), message.end());
        const NativeInputResult result = input.Dispatch(json);
        const std::string acknowledgement = "{\"type\":\"control-result\",\"ok\":" +
            std::string(result.accepted ? "true" : "false") + ",\"stage\":\"" + JsonLite::Escape(result.stage) +
            "\",\"error\":" + std::to_string(result.error) + ",\"desktop\":\"" + JsonLite::Escape(result.desktop) +
            "\",\"eventType\":\"" + JsonLite::Escape(result.eventType) + "\"}";
        socket.SendText(acknowledgement);
    }
}

void SessionRuntime::VideoLoop(NativeWebSocket& socket) {
    const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(com)) throw std::runtime_error("CoInitializeEx failed for video thread");
    struct ComCloser { ~ComCloser() { CoUninitialize(); } } comCloser;
    if (GetSystemMetrics(SM_CMONITORS) > 1) {
        Diagnostics::Write(L"Multiple monitors detected; full virtual desktop will use native GDI/WIC JPEG capture.");
        GdiVideoLoop(socket);
        return;
    }
    try {
        DxgiVideoLoop(socket);
        return;
    } catch (const std::exception& error) {
        if (stopping_.load()) return;
        const std::string narrow(error.what());
        Diagnostics::Write(L"DXGI/H.264 unavailable; native GDI/WIC JPEG fallback activated. Cause=" +
            std::wstring(narrow.begin(), narrow.end()));
    }
    GdiVideoLoop(socket);
}

void SessionRuntime::DxgiVideoLoop(NativeWebSocket& socket) {
    DesktopDuplicator duplicator(0);
    const auto description = duplicator.Description();
    const std::uint32_t width = description.ModeDesc.Width;
    const std::uint32_t height = description.ModeDesc.Height;
    GpuColorConverter converter(duplicator.Device(), width, height);
    H264Encoder encoder(duplicator.Device(), width, height, 30, 3'000'000);
    std::int64_t frameNumber = 0;
    bool firstFrame = true;
    while (!stopping_.load() && socket.IsOpen()) {
        CapturedDesktopFrame frame;
        if (!duplicator.TryAcquire(frame, 100)) continue;
        const auto nv12 = converter.Convert(frame.texture.Get());
        const auto encoded = encoder.Encode(nv12.Get(), frameNumber++ * (10'000'000LL / 30));
        duplicator.ReleaseFrame();
        if (encoded.bytes.empty()) continue;
        std::vector<std::uint8_t> packet(14 + encoded.bytes.size());
        packet[0] = 4;
        packet[1] = encoded.cleanPoint ? 1 : 0;
        packet[2] = static_cast<std::uint8_t>(width & 0xFF);
        packet[3] = static_cast<std::uint8_t>((width >> 8) & 0xFF);
        packet[4] = static_cast<std::uint8_t>(height & 0xFF);
        packet[5] = static_cast<std::uint8_t>((height >> 8) & 0xFF);
        const std::int64_t timestamp = encoded.timestamp100ns;
        std::memcpy(packet.data() + 6, &timestamp, sizeof(timestamp));
        std::memcpy(packet.data() + 14, encoded.bytes.data(), encoded.bytes.size());
        socket.SendBinary(packet);
        if (firstFrame) {
            Diagnostics::Write(L"First native DXGI/H.264 frame sent. Resolution=" + std::to_wstring(width) + L"x" +
                std::to_wstring(height) + L", Bytes=" + std::to_wstring(packet.size()));
            firstFrame = false;
        }
    }
}

void SessionRuntime::GdiVideoLoop(NativeWebSocket& socket) {
    GdiJpegCapture capture(1440, 900);
    bool firstFrame = true;
    const auto frameInterval = std::chrono::milliseconds(70);
    while (!stopping_.load() && socket.IsOpen()) {
        const auto started = std::chrono::steady_clock::now();
        const auto packet = capture.CapturePacket();
        socket.SendBinary(packet);
        if (firstFrame) {
            Diagnostics::Write(L"First native GDI/WIC JPEG frame sent. Resolution=" +
                std::to_wstring(capture.Width()) + L"x" + std::to_wstring(capture.Height()) +
                L", Bytes=" + std::to_wstring(packet.size()));
            firstFrame = false;
        }
        const auto elapsed = std::chrono::steady_clock::now() - started;
        if (elapsed < frameInterval) std::this_thread::sleep_for(frameInterval - elapsed);
    }
}
