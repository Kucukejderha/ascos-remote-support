#include "SharedFrameBuffer.h"
#include <cstring>
#include <limits>
#include <stdexcept>
#include <system_error>

namespace {
std::wstring ObjectName(const wchar_t* kind, std::uint32_t sessionId) {
    return std::wstring(L"Global\\RotaLink.") + kind + L"." + std::to_wstring(sessionId);
}

[[noreturn]] void ThrowLastError(const char* operation) {
    throw std::system_error(static_cast<int>(GetLastError()), std::system_category(), operation);
}
}

SharedFrameBuffer::SharedFrameBuffer(std::uint32_t sessionId, std::size_t capacityBytes) : capacity_(capacityBytes) {
    if (capacityBytes == 0 || capacityBytes > std::numeric_limits<std::uint32_t>::max())
        throw std::invalid_argument("Shared frame capacity is invalid");
    const auto total = sizeof(SharedFrameHeader) + capacityBytes;
    const DWORD high = static_cast<DWORD>((static_cast<std::uint64_t>(total) >> 32) & 0xFFFFFFFFu);
    const DWORD low = static_cast<DWORD>(total & 0xFFFFFFFFu);
    const auto mappingName = ObjectName(L"FrameMap", sessionId);
    mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, high, low, mappingName.c_str());
    if (!mapping_) ThrowLastError("CreateFileMappingW");
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        CloseHandle(mapping_); mapping_ = nullptr;
        throw std::runtime_error("A native capture publisher already owns this session mapping");
    }
    view_ = MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, total);
    if (!view_) ThrowLastError("MapViewOfFile");
    const auto eventName = ObjectName(L"FrameReady", sessionId);
    readyEvent_ = CreateEventW(nullptr, FALSE, FALSE, eventName.c_str());
    if (!readyEvent_) ThrowLastError("CreateEventW");

    std::memset(view_, 0, total);
    header_ = static_cast<SharedFrameHeader*>(view_);
    payload_ = reinterpret_cast<std::uint8_t*>(view_) + sizeof(SharedFrameHeader);
    header_->magic = Magic;
    header_->version = Version;
    header_->headerBytes = static_cast<std::uint16_t>(sizeof(SharedFrameHeader));
    header_->capacityBytes = static_cast<std::uint32_t>(capacityBytes);
    header_->codec = CodecH264AnnexB;
}

SharedFrameBuffer::~SharedFrameBuffer() {
    if (view_) UnmapViewOfFile(view_);
    if (readyEvent_) CloseHandle(readyEvent_);
    if (mapping_) CloseHandle(mapping_);
}

void SharedFrameBuffer::Publish(std::span<const std::uint8_t> data, std::uint32_t width, std::uint32_t height,
    std::int64_t timestamp100ns, bool keyFrame) {
    if (data.empty() || data.size() > capacity_) throw std::length_error("Encoded frame exceeds shared memory capacity");
    InterlockedIncrement64(&header_->sequence); // Odd: writer owns the slot.
    MemoryBarrier();
    std::memcpy(payload_, data.data(), data.size());
    header_->payloadBytes = static_cast<std::uint32_t>(data.size());
    header_->width = width;
    header_->height = height;
    header_->codec = CodecH264AnnexB;
    header_->flags = keyFrame ? FlagKeyFrame : 0;
    header_->timestamp100ns = timestamp100ns;
    MemoryBarrier();
    InterlockedIncrement64(&header_->sequence); // Even: stable frame is visible.
    if (!SetEvent(readyEvent_)) ThrowLastError("SetEvent");
}
