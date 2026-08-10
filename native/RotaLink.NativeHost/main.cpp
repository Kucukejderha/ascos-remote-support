#include "DesktopDuplicator.h"
#include "GpuColorConverter.h"
#include "H264Encoder.h"
#include "SharedFrameBuffer.h"
#include <charconv>
#include <chrono>
#include <iostream>
#include <stdexcept>
#include <string_view>
#include <thread>
#include <windows.h>

class ComApartment final {
public:
    ComApartment() {
        const HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(result)) throw std::runtime_error("CoInitializeEx failed");
    }
    ~ComApartment() { CoUninitialize(); }
};

std::uint32_t ParseUnsigned(const wchar_t* text, const char* name) {
    wchar_t* end{};
    const auto value = wcstoul(text, &end, 10);
    if (!text[0] || !end || *end != L'\0' || value > UINT32_MAX) throw std::invalid_argument(name);
    return static_cast<std::uint32_t>(value);
}

int wmain(int argc, wchar_t** argv) {
    try {
        std::uint32_t sessionId = 0xFFFFFFFFu, outputIndex = 0;
        bool probe = false;
        for (int index = 1; index < argc; ++index) {
            const std::wstring_view argument(argv[index]);
            if (argument == L"--session" && index + 1 < argc) sessionId = ParseUnsigned(argv[++index], "Invalid session id");
            else if (argument == L"--output" && index + 1 < argc) outputIndex = ParseUnsigned(argv[++index], "Invalid output index");
            else if (argument == L"--probe") probe = true;
            else throw std::invalid_argument("Unknown native capture argument");
        }
        if (!probe && sessionId == 0xFFFFFFFFu) throw std::invalid_argument("--session is required");

        ComApartment apartment;
        DesktopDuplicator duplicator(outputIndex);
        const auto description = duplicator.Description();
        const auto width = description.ModeDesc.Width, height = description.ModeDesc.Height;
        GpuColorConverter converter(duplicator.Device(), width, height);
        H264Encoder encoder(duplicator.Device(), width, height, 30, 3'000'000);
        std::unique_ptr<SharedFrameBuffer> shared;
        if (!probe) shared = std::make_unique<SharedFrameBuffer>(sessionId, 4 * 1024 * 1024);

        const auto started = std::chrono::steady_clock::now();
        std::uint64_t frameNumber = 0, dropped = 0;
        for (;;) {
            CapturedDesktopFrame frame;
            if (!duplicator.TryAcquire(frame, 100)) {
                if (probe && std::chrono::steady_clock::now() - started > std::chrono::seconds(5)) break;
                continue;
            }
            const auto nv12 = converter.Convert(frame.texture.Get());
            const auto timestamp = static_cast<std::int64_t>(frameNumber) * (10'000'000LL / 30);
            const auto encoded = encoder.Encode(nv12.Get(), timestamp);
            dropped += frame.accumulatedFrames > 1 ? frame.accumulatedFrames - 1 : 0;
            duplicator.ReleaseFrame();
            if (!encoded.bytes.empty() && shared)
                shared->Publish(encoded.bytes, width, height, encoded.timestamp100ns, encoded.cleanPoint);
            ++frameNumber;
            if (probe && std::chrono::steady_clock::now() - started > std::chrono::seconds(5)) break;
        }
        if (probe) std::wcout << L"Frames=" << frameNumber << L", dropped=" << dropped << L"\n";
        return frameNumber == 0 ? 2 : 0;
    } catch (const std::exception& error) {
        std::cerr << "Native capture failed: " << error.what() << '\n';
        return 1;
    }
}
