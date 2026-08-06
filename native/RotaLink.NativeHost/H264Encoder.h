#pragma once
#include <cstdint>
#include <vector>
#include <wrl/client.h>
#include <d3d11.h>
#include <mfidl.h>
#include <mftransform.h>

struct EncodedVideoPacket final {
    std::vector<std::uint8_t> bytes;
    std::int64_t timestamp100ns{};
    bool cleanPoint{};
};

class H264Encoder final {
public:
    H264Encoder(ID3D11Device* device, std::uint32_t width, std::uint32_t height,
        std::uint32_t framesPerSecond = 30, std::uint32_t bitrate = 2'500'000);
    H264Encoder(const H264Encoder&) = delete;
    H264Encoder& operator=(const H264Encoder&) = delete;
    ~H264Encoder();
    EncodedVideoPacket Encode(ID3D11Texture2D* nv12Texture, std::int64_t timestamp100ns);
private:
    void Configure(ID3D11Device* device);
    Microsoft::WRL::ComPtr<IMFTransform> transform_;
    Microsoft::WRL::ComPtr<IMFDXGIDeviceManager> deviceManager_;
    std::uint32_t resetToken_{}, width_{}, height_{}, framesPerSecond_{}, bitrate_{};
    bool started_{};
};
