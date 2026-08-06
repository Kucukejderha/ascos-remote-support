#include "DesktopDuplicator.h"
#include "GpuColorConverter.h"
#include "H264Encoder.h"
#include <chrono>
#include <iostream>
#include <stdexcept>

class ComApartment final {
public:
    ComApartment() {
        const HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(result)) throw std::runtime_error("CoInitializeEx failed");
    }
    ~ComApartment() { CoUninitialize(); }
};

int wmain() {
    try {
        ComApartment apartment;
        DesktopDuplicator duplicator;
        const auto description = duplicator.Description();
        GpuColorConverter converter(duplicator.Device(), description.ModeDesc.Width, description.ModeDesc.Height);
        H264Encoder encoder(duplicator.Device(), description.ModeDesc.Width, description.ModeDesc.Height);
        std::wcout << L"RotaLink DXGI capture probe\nDesktop=" << description.ModeDesc.Width << L"x" << description.ModeDesc.Height << L"\n";
        const auto start = std::chrono::steady_clock::now();
        std::uint64_t frames = 0, accumulated = 0, encodedBytes = 0;
        while (std::chrono::steady_clock::now() - start < std::chrono::seconds(5)) {
            CapturedDesktopFrame frame;
            if (!duplicator.TryAcquire(frame, 100)) continue;
            const auto nv12 = converter.Convert(frame.texture.Get());
            if (!nv12) return 3;
            const auto packet = encoder.Encode(nv12.Get(), static_cast<std::int64_t>(frames) * (10'000'000LL / 30));
            encodedBytes += packet.bytes.size();
            ++frames; accumulated += frame.accumulatedFrames;
            duplicator.ReleaseFrame();
        }
        const auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
        std::wcout << L"Frames=" << frames << L", accumulated=" << accumulated << L", encodedBytes=" << encodedBytes
                   << L", observedFPS=" << frames / elapsed << L"\n";
        return frames == 0 ? 2 : 0;
    } catch (const std::exception& error) {
        std::cerr << "Capture probe failed: " << error.what() << '\n';
        return 1;
    }
}
