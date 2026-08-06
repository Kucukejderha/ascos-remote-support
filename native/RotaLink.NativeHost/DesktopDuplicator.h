#pragma once
#include <cstdint>
#include <wrl/client.h>
#include <d3d11.h>
#include <dxgi1_2.h>

struct CapturedDesktopFrame final {
    Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
    std::uint32_t width{}, height{}, accumulatedFrames{};
    std::uint64_t presentationTime100ns{};
};

class DesktopDuplicator final {
public:
    DesktopDuplicator();
    DesktopDuplicator(const DesktopDuplicator&) = delete;
    DesktopDuplicator& operator=(const DesktopDuplicator&) = delete;
    ~DesktopDuplicator();
    bool TryAcquire(CapturedDesktopFrame& frame, std::uint32_t timeoutMilliseconds);
    void ReleaseFrame() noexcept;
    [[nodiscard]] DXGI_OUTDUPL_DESC Description() const noexcept;
    [[nodiscard]] ID3D11Device* Device() const noexcept { return device_.Get(); }
    [[nodiscard]] ID3D11DeviceContext* Context() const noexcept { return context_.Get(); }
private:
    void Initialize();
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<IDXGIOutputDuplication> duplication_;
    DXGI_OUTDUPL_DESC description_{};
    bool frameHeld_{};
};
