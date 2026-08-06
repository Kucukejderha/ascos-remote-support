#include "DesktopDuplicator.h"
#include "GpuColorConverter.h"
#include <chrono>
#include <iostream>

int wmain() {
    try {
        DesktopDuplicator duplicator;
        const auto description = duplicator.Description();
        GpuColorConverter converter(duplicator.Device(), description.ModeDesc.Width, description.ModeDesc.Height);
        std::wcout << L"RotaLink DXGI capture probe\nDesktop=" << description.ModeDesc.Width << L"x" << description.ModeDesc.Height << L"\n";
        const auto start = std::chrono::steady_clock::now();
        std::uint64_t frames = 0, accumulated = 0;
        while (std::chrono::steady_clock::now() - start < std::chrono::seconds(5)) {
            CapturedDesktopFrame frame;
            if (!duplicator.TryAcquire(frame, 100)) continue;
            const auto nv12 = converter.Convert(frame.texture.Get());
            if (!nv12) return 3;
            ++frames; accumulated += frame.accumulatedFrames;
            duplicator.ReleaseFrame();
        }
        const auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
        std::wcout << L"Frames=" << frames << L", accumulated=" << accumulated << L", observedFPS=" << frames / elapsed << L"\n";
        return frames == 0 ? 2 : 0;
    } catch (const std::exception& error) {
        std::cerr << "Capture probe failed: " << error.what() << '\n';
        return 1;
    }
}
