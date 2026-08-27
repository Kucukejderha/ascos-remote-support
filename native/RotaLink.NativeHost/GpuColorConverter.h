#pragma once
#include <cstdint>
#include "ComPtr.h"
#include <d3d11.h>

class GpuColorConverter final {
public:
    GpuColorConverter(ID3D11Device* device, std::uint32_t width, std::uint32_t height);
    ComPtr<ID3D11Texture2D> Convert(ID3D11Texture2D* bgraTexture);
private:
    std::uint32_t width_{}, height_{};
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11VideoDevice> videoDevice_;
    ComPtr<ID3D11VideoContext> videoContext_;
    ComPtr<ID3D11VideoProcessorEnumerator> enumerator_;
    ComPtr<ID3D11VideoProcessor> processor_;
};

