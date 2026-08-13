#include "GdiJpegCapture.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;

namespace {
[[noreturn]] void ThrowHr(const char* operation, HRESULT error) {
    throw std::runtime_error(std::string(operation) + " failed, HRESULT=" +
        std::to_string(static_cast<unsigned long>(error)));
}

void Check(HRESULT error, const char* operation) {
    if (FAILED(error)) ThrowHr(operation, error);
}

[[noreturn]] void ThrowLastError(const char* operation) {
    throw std::runtime_error(std::string(operation) + " failed, Win32=" + std::to_string(GetLastError()));
}
}

GdiJpegCapture::GdiJpegCapture(std::uint32_t maximumWidth, std::uint32_t maximumHeight) {
    try {
        sourceX_ = GetSystemMetrics(SM_XVIRTUALSCREEN);
        sourceY_ = GetSystemMetrics(SM_YVIRTUALSCREEN);
        sourceWidth_ = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        sourceHeight_ = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (sourceWidth_ <= 0 || sourceHeight_ <= 0 || maximumWidth == 0 || maximumHeight == 0)
            throw std::runtime_error("Virtual desktop metrics are invalid");
        const double scale = (std::min)(1.0, (std::min)(
            static_cast<double>(maximumWidth) / sourceWidth_,
            static_cast<double>(maximumHeight) / sourceHeight_));
        width_ = (std::max)(1u, static_cast<std::uint32_t>(std::lround(sourceWidth_ * scale)));
        height_ = (std::max)(1u, static_cast<std::uint32_t>(std::lround(sourceHeight_ * scale)));
        // H.264 and many legacy codecs require even dimensions; using even JPEG
        // dimensions also keeps input/video geometry identical across fallbacks.
        if (width_ > 1) width_ &= ~1u;
        if (height_ > 1) height_ &= ~1u;
        stride_ = width_ * 4;
        screen_ = GetDC(nullptr);
        if (!screen_) ThrowLastError("GetDC");
        memory_ = CreateCompatibleDC(screen_);
        if (!memory_) ThrowLastError("CreateCompatibleDC");
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = static_cast<LONG>(width_);
        info.bmiHeader.biHeight = -static_cast<LONG>(height_);
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        bitmap_ = CreateDIBSection(memory_, &info, DIB_RGB_COLORS, &pixels_, nullptr, 0);
        if (!bitmap_ || !pixels_) ThrowLastError("CreateDIBSection");
        previousBitmap_ = SelectObject(memory_, bitmap_);
        if (!previousBitmap_ || previousBitmap_ == HGDI_ERROR) ThrowLastError("SelectObject");
        Check(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&factory_)), "CoCreateInstance(WIC)");
    } catch (...) {
        ReleaseResources();
        throw;
    }
}

GdiJpegCapture::~GdiJpegCapture() {
    ReleaseResources();
}

void GdiJpegCapture::ReleaseResources() noexcept {
    factory_.Reset();
    if (memory_ && previousBitmap_ && previousBitmap_ != HGDI_ERROR) SelectObject(memory_, previousBitmap_);
    if (bitmap_) DeleteObject(bitmap_);
    if (memory_) DeleteDC(memory_);
    if (screen_) ReleaseDC(nullptr, screen_);
    previousBitmap_ = nullptr;
    bitmap_ = nullptr;
    memory_ = nullptr;
    screen_ = nullptr;
    pixels_ = nullptr;
}

std::vector<std::uint8_t> GdiJpegCapture::CapturePacket() {
    SetStretchBltMode(memory_, HALFTONE);
    SetBrushOrgEx(memory_, 0, 0, nullptr);
    if (!StretchBlt(memory_, 0, 0, static_cast<int>(width_), static_cast<int>(height_), screen_,
        sourceX_, sourceY_, sourceWidth_, sourceHeight_, SRCCOPY | CAPTUREBLT)) ThrowLastError("StretchBlt");
    auto jpeg = EncodeJpeg();
    std::vector<std::uint8_t> packet(5 + jpeg.size());
    packet[0] = 3;
    packet[1] = static_cast<std::uint8_t>(width_ & 0xFF);
    packet[2] = static_cast<std::uint8_t>((width_ >> 8) & 0xFF);
    packet[3] = static_cast<std::uint8_t>(height_ & 0xFF);
    packet[4] = static_cast<std::uint8_t>((height_ >> 8) & 0xFF);
    std::memcpy(packet.data() + 5, jpeg.data(), jpeg.size());
    return packet;
}

std::vector<std::uint8_t> GdiJpegCapture::EncodeJpeg() const {
    ComPtr<IStream> stream;
    Check(CreateStreamOnHGlobal(nullptr, TRUE, &stream), "CreateStreamOnHGlobal");
    ComPtr<IWICStream> wicStream;
    Check(factory_->CreateStream(&wicStream), "IWICImagingFactory::CreateStream");
    Check(wicStream->InitializeFromIStream(stream.Get()), "IWICStream::InitializeFromIStream");
    ComPtr<IWICBitmapEncoder> encoder;
    Check(factory_->CreateEncoder(GUID_ContainerFormatJpeg, nullptr, &encoder), "CreateEncoder(JPEG)");
    Check(encoder->Initialize(wicStream.Get(), WICBitmapEncoderNoCache), "JPEG encoder Initialize");
    ComPtr<IWICBitmapFrameEncode> frame;
    ComPtr<IPropertyBag2> properties;
    Check(encoder->CreateNewFrame(&frame, &properties), "JPEG CreateNewFrame");
    if (properties) {
        PROPBAG2 option{};
        option.pstrName = const_cast<LPOLESTR>(L"ImageQuality");
        VARIANT value{};
        VariantInit(&value);
        value.vt = VT_R4;
        value.fltVal = 0.82f;
        properties->Write(1, &option, &value);
        VariantClear(&value);
    }
    Check(frame->Initialize(properties.Get()), "JPEG frame Initialize");
    Check(frame->SetSize(width_, height_), "JPEG SetSize");
    WICPixelFormatGUID format = GUID_WICPixelFormat24bppBGR;
    Check(frame->SetPixelFormat(&format), "JPEG SetPixelFormat");
    ComPtr<IWICBitmap> bitmap;
    Check(factory_->CreateBitmapFromMemory(width_, height_, GUID_WICPixelFormat32bppBGRA, stride_,
        stride_ * height_, static_cast<BYTE*>(pixels_), &bitmap), "CreateBitmapFromMemory");
    ComPtr<IWICFormatConverter> converter;
    Check(factory_->CreateFormatConverter(&converter), "CreateFormatConverter");
    Check(converter->Initialize(bitmap.Get(), GUID_WICPixelFormat24bppBGR, WICBitmapDitherTypeNone,
        nullptr, 0.0, WICBitmapPaletteTypeCustom), "WIC format conversion");
    Check(frame->WriteSource(converter.Get(), nullptr), "JPEG WriteSource");
    Check(frame->Commit(), "JPEG frame Commit");
    Check(encoder->Commit(), "JPEG encoder Commit");
    STATSTG statistics{};
    Check(stream->Stat(&statistics, STATFLAG_NONAME), "IStream::Stat");
    if (statistics.cbSize.QuadPart <= 0 || statistics.cbSize.QuadPart > 16LL * 1024 * 1024)
        throw std::runtime_error("Encoded JPEG size is invalid");
    LARGE_INTEGER beginning{};
    Check(stream->Seek(beginning, STREAM_SEEK_SET, nullptr), "IStream::Seek");
    std::vector<std::uint8_t> output(static_cast<std::size_t>(statistics.cbSize.QuadPart));
    ULONG read = 0;
    Check(stream->Read(output.data(), static_cast<ULONG>(output.size()), &read), "IStream::Read");
    if (read != output.size()) throw std::runtime_error("Encoded JPEG stream ended unexpectedly");
    return output;
}
