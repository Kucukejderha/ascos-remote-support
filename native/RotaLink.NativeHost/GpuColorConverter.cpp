#include "GpuColorConverter.h"
#include <stdexcept>
#include <string>


namespace {
void Check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) throw std::runtime_error(std::string(operation) + " failed");
}
}

GpuColorConverter::GpuColorConverter(ID3D11Device* device, std::uint32_t width, std::uint32_t height)
    : width_(width), height_(height), device_(device) {
    Check(device_->QueryInterface(IID_PPV_ARGS(&videoDevice_)), "ID3D11VideoDevice");
    ComPtr<ID3D11DeviceContext> context;
    device_->GetImmediateContext(&context);
    Check(context.As(&videoContext_), "ID3D11VideoContext");
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
    content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    content.InputFrameRate = { 30, 1 };
    content.InputWidth = width_;
    content.InputHeight = height_;
    content.OutputFrameRate = { 30, 1 };
    content.OutputWidth = width_;
    content.OutputHeight = height_;
    content.Usage = D3D11_VIDEO_USAGE_OPTIMAL_SPEED;
    Check(videoDevice_->CreateVideoProcessorEnumerator(&content, &enumerator_), "CreateVideoProcessorEnumerator");
    Check(videoDevice_->CreateVideoProcessor(enumerator_.Get(), 0, &processor_), "CreateVideoProcessor");
}

ComPtr<ID3D11Texture2D> GpuColorConverter::Convert(ID3D11Texture2D* bgraTexture) {
    D3D11_TEXTURE2D_DESC outputDescription{};
    outputDescription.Width = width_;
    outputDescription.Height = height_;
    outputDescription.MipLevels = 1;
    outputDescription.ArraySize = 1;
    outputDescription.Format = DXGI_FORMAT_NV12;
    outputDescription.SampleDesc = { 1, 0 };
    outputDescription.Usage = D3D11_USAGE_DEFAULT;
    outputDescription.BindFlags = D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> output;
    Check(device_->CreateTexture2D(&outputDescription, nullptr, &output), "Create NV12 texture");

    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription{};
    inputDescription.FourCC = 0;
    inputDescription.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    inputDescription.Texture2D = { 0, 0 };
    ComPtr<ID3D11VideoProcessorInputView> inputView;
    Check(videoDevice_->CreateVideoProcessorInputView(bgraTexture, enumerator_.Get(), &inputDescription, &inputView), "Create input view");

    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputViewDescription{};
    outputViewDescription.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    outputViewDescription.Texture2D.MipSlice = 0;
    ComPtr<ID3D11VideoProcessorOutputView> outputView;
    Check(videoDevice_->CreateVideoProcessorOutputView(output.Get(), enumerator_.Get(), &outputViewDescription, &outputView), "Create output view");

    const RECT source{ 0, 0, static_cast<LONG>(width_), static_cast<LONG>(height_) };
    videoContext_->VideoProcessorSetStreamSourceRect(processor_.Get(), 0, TRUE, &source);
    videoContext_->VideoProcessorSetStreamDestRect(processor_.Get(), 0, TRUE, &source);
    videoContext_->VideoProcessorSetOutputTargetRect(processor_.Get(), TRUE, &source);
    D3D11_VIDEO_PROCESSOR_STREAM stream{};
    stream.Enable = TRUE;
    stream.pInputSurface = inputView.Get();
    Check(videoContext_->VideoProcessorBlt(processor_.Get(), outputView.Get(), 0, 1, &stream), "VideoProcessorBlt");
    return output;
}

