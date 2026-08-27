#include "DesktopDuplicator.h"
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <string>
#include <system_error>

namespace {
[[noreturn]] void ThrowHr(const char* operation, HRESULT hr) {
    std::ostringstream message;
    message << operation << " failed, HRESULT=0x" << std::uppercase << std::hex << std::setfill('0') << std::setw(8)
        << static_cast<unsigned long>(hr);
    throw std::runtime_error(message.str());
}
}

DesktopDuplicator::DesktopDuplicator(std::uint32_t outputIndex) : outputIndex_(outputIndex) { Initialize(); }
DesktopDuplicator::~DesktopDuplicator() {
    ReleaseFrame();
    // The process is about to leave the desktop-bound capture thread. Windows
    // may reject CloseDesktop while it is still assigned; process teardown then
    // closes the final handle. Never close it before all D3D objects are gone.
    duplication_.Reset(); context_.Reset(); device_.Reset();
    if (inputDesktop_) CloseDesktop(inputDesktop_);
}

void DesktopDuplicator::AttachInputDesktop() {
    constexpr ACCESS_MASK access = DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_CREATEMENU |
        DESKTOP_WRITEOBJECTS | DESKTOP_SWITCHDESKTOP;
    HDESK next = OpenInputDesktop(0, FALSE, access);
    if (!next) throw std::system_error(static_cast<int>(GetLastError()), std::system_category(), "OpenInputDesktop");
    if (!SetThreadDesktop(next)) {
        const auto error = GetLastError(); CloseDesktop(next);
        throw std::system_error(static_cast<int>(error), std::system_category(), "SetThreadDesktop");
    }
    const auto previous = inputDesktop_;
    inputDesktop_ = next;
    if (previous) CloseDesktop(previous);
}

void DesktopDuplicator::Initialize() {
    AttachInputDesktop();
    constexpr D3D_FEATURE_LEVEL requested[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL selected{};
    const UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, requested,
        ARRAYSIZE(requested), D3D11_SDK_VERSION, &device_, &selected, &context_);
    if (FAILED(hr)) ThrowHr("D3D11CreateDevice", hr);
    CreateDuplication();
}

void DesktopDuplicator::CreateDuplication() {
    HRESULT hr{};
    ComPtr<IDXGIDevice> dxgiDevice;
    if (FAILED(hr = device_.As(&dxgiDevice))) ThrowHr("IDXGIDevice", hr);
    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(hr = dxgiDevice->GetAdapter(&adapter))) ThrowHr("GetAdapter", hr);
    ComPtr<IDXGIOutput> output;
    if (FAILED(hr = adapter->EnumOutputs(outputIndex_, &output))) ThrowHr("EnumOutputs", hr);
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
    if (hr == DXGI_ERROR_ACCESS_LOST || hr == DXGI_ERROR_SESSION_DISCONNECTED) {
        Reinitialize();
        return false;
    }
    if (hr == DXGI_ERROR_DEVICE_REMOVED) ThrowHr("D3D11 device removed", device_->GetDeviceRemovedReason());
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

void DesktopDuplicator::Reinitialize() {
    ReleaseFrame();
    duplication_.Reset();
    description_ = {};
    AttachInputDesktop();
    CreateDuplication();
}

void DesktopDuplicator::ReleaseFrame() noexcept {
    if (frameHeld_ && duplication_) duplication_->ReleaseFrame();
    frameHeld_ = false;
}
DXGI_OUTDUPL_DESC DesktopDuplicator::Description() const noexcept { return description_; }

