#pragma once

#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <cstdint>
#include <vector>

class GdiJpegCapture final {
public:
    GdiJpegCapture(std::uint32_t maximumWidth, std::uint32_t maximumHeight);
    ~GdiJpegCapture();
    GdiJpegCapture(const GdiJpegCapture&) = delete;
    GdiJpegCapture& operator=(const GdiJpegCapture&) = delete;
    [[nodiscard]] std::vector<std::uint8_t> CapturePacket();
    [[nodiscard]] std::uint32_t Width() const noexcept { return width_; }
    [[nodiscard]] std::uint32_t Height() const noexcept { return height_; }
private:
    [[nodiscard]] std::vector<std::uint8_t> EncodeJpeg() const;
    void ReleaseResources() noexcept;
    int sourceX_{};
    int sourceY_{};
    int sourceWidth_{};
    int sourceHeight_{};
    std::uint32_t width_{};
    std::uint32_t height_{};
    HDC screen_{};
    HDC memory_{};
    HBITMAP bitmap_{};
    HGDIOBJ previousBitmap_{};
    void* pixels_{};
    std::uint32_t stride_{};
    Microsoft::WRL::ComPtr<IWICImagingFactory> factory_;
};
