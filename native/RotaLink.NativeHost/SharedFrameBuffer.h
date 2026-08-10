#pragma once
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <windows.h>

#pragma pack(push, 8)
struct SharedFrameHeader final {
    std::uint32_t magic;
    std::uint16_t version;
    std::uint16_t headerBytes;
    std::uint32_t capacityBytes;
    std::uint32_t payloadBytes;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t codec;
    std::uint32_t flags;
    alignas(8) volatile LONG64 sequence;
    std::int64_t timestamp100ns;
    std::uint8_t reserved[16];
};
#pragma pack(pop)

class SharedFrameBuffer final {
public:
    static constexpr std::uint32_t Magic = 0x4D465452; // RTFM
    static constexpr std::uint16_t Version = 1;
    static constexpr std::uint32_t CodecH264AnnexB = 3;
    static constexpr std::uint32_t FlagKeyFrame = 1;

    SharedFrameBuffer(std::uint32_t sessionId, std::size_t capacityBytes);
    SharedFrameBuffer(const SharedFrameBuffer&) = delete;
    SharedFrameBuffer& operator=(const SharedFrameBuffer&) = delete;
    ~SharedFrameBuffer();

    void Publish(std::span<const std::uint8_t> payload, std::uint32_t width, std::uint32_t height,
        std::int64_t timestamp100ns, bool keyFrame);

private:
    HANDLE mapping_{};
    HANDLE readyEvent_{};
    void* view_{};
    SharedFrameHeader* header_{};
    std::uint8_t* payload_{};
    std::size_t capacity_{};
};
