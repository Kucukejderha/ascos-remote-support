#include "DesktopDuplicator.h"
#include <stdexcept>
#include <string>
using Microsoft::WRL::ComPtr;

namespace {
[[noreturn]] void ThrowHr(const char* operation, HRESULT hr) {
    throw std::runtime_error(std::string(operation) + " failed, HRESULT=" + std::to_string(static_cast<unsigned long>(hr)));
}
}

DesktopDuplicator::DesktopDuplicator() { Initialize(); }
DesktopDuplicator::~DesktopDuplicator() { ReleaseFrame(); }

void DesktopDuplicator::Initialize() {
    constexpr D3D_FEATURE_LEVEL requested[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL selected{};
    const UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, requested,
        ARRAYSIZE(requested), D3D11_SDK_VERSION, &device_, &selected, &context_);
    if (FAILED(hr)) ThrowHr("D3D11CreateDevice", hr);
    ComPtr<IDXGIDevice> dxgiDevice;
    if (FAILED(hr = device_.As(&dxgiDevice))) ThrowHr("IDXGIDevice", hr);
    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(hr = dxgiDevice->GetAdapter(&adapter))) ThrowHr("GetAdapter", hr);
    ComPtr<IDXGIOutput> output;
    if (FAILED(hr = adapter->EnumOutputs(0, &output))) ThrowHr("EnumOutputs", hr);
    ComPtr<IDXGIOutput1> output1;
    if (FAILED(hr = output.As(&output1))) ThrowHr("IDXGIOutput1", hr);
    if (FAILED(hr = output1->DuplicateOutput(device_.Get(), &duplication_))) ThrowHr("DuplicateOutput", hr);
    duplication_->GetDesc(&description_);
}

bool DesktopDuplicator::TryAcquire(CapturedDesktopFrame& frame, std::uint32_t timeoutMilliseconds) {
    ReleaseFrame();
    DXGI_OUTDUPL_FRAME_INFO info{};
    ComPtr<IDXGIResource> resource;
    HRESULT hr = duplication_->AcquireNextFrame(timeoutMilliseconds, &info, &resource);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return false;
    if (FAILED(hr)) ThrowHr("AcquireNextFrame", hr);
    frameHeld_ = true;
    hr = resource.As(&frame.texture);
    if (FAILED(hr)) { ReleaseFrame(); ThrowHr("Desktop texture query", hr); }
    D3D11_TEXTURE2D_DESC desc{};
    frame.texture->GetDesc(&desc);
    frame.width = desc.Width; frame.height = desc.Height;
    frame.presentationTime100ns = static_cast<std::uint64_t>(info.LastPresentTime.QuadPart);
    frame.accumulatedFrames = info.AccumulatedFrames;
    return true;
}

void DesktopDuplicator::ReleaseFrame() noexcept {
    if (frameHeld_ && duplication_) duplication_->ReleaseFrame();
    frameHeld_ = false;
}
DXGI_OUTDUPL_DESC DesktopDuplicator::Description() const noexcept { return description_; }
