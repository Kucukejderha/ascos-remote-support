#include "H264Encoder.h"
#include <codecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;

namespace {
void Check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) throw std::runtime_error(std::string(operation) + " failed, HRESULT=" + std::to_string(static_cast<unsigned long>(hr)));
}

ComPtr<IMFMediaType> VideoType(const GUID& subtype, std::uint32_t width, std::uint32_t height, std::uint32_t fps) {
    ComPtr<IMFMediaType> type;
    Check(MFCreateMediaType(&type), "MFCreateMediaType");
    Check(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "MF_MT_MAJOR_TYPE");
    Check(type->SetGUID(MF_MT_SUBTYPE, subtype), "MF_MT_SUBTYPE");
    Check(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, width, height), "MF_MT_FRAME_SIZE");
    Check(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, fps, 1), "MF_MT_FRAME_RATE");
    Check(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1), "MF_MT_PIXEL_ASPECT_RATIO");
    Check(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "MF_MT_INTERLACE_MODE");
    return type;
}
}

H264Encoder::H264Encoder(ID3D11Device* device, std::uint32_t width, std::uint32_t height,
    std::uint32_t framesPerSecond, std::uint32_t bitrate)
    : width_(width), height_(height), framesPerSecond_(framesPerSecond), bitrate_(bitrate) {
    Check(MFStartup(MF_VERSION, MFSTARTUP_LITE), "MFStartup");
    Configure(device);
}

H264Encoder::~H264Encoder() {
    if (transform_) {
        transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        transform_->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
    }
    MFShutdown();
}

void H264Encoder::Configure(ID3D11Device* device) {
    MFT_REGISTER_TYPE_INFO inputInfo{ MFMediaType_Video, MFVideoFormat_NV12 };
    MFT_REGISTER_TYPE_INFO outputInfo{ MFMediaType_Video, MFVideoFormat_H264 };
    IMFActivate** activations = nullptr;
    UINT32 activationCount = 0;
    const UINT32 flags = MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER;
    Check(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, flags, &inputInfo, &outputInfo, &activations, &activationCount), "MFTEnumEx");
    if (activationCount == 0) throw std::runtime_error("No synchronous H.264 encoder is installed");
    const HRESULT activationResult = activations[0]->ActivateObject(IID_PPV_ARGS(&transform_));
    for (UINT32 index = 0; index < activationCount; ++index) activations[index]->Release();
    CoTaskMemFree(activations);
    Check(activationResult, "Activate H.264 encoder");

    auto outputType = VideoType(MFVideoFormat_H264, width_, height_, framesPerSecond_);
    Check(outputType->SetUINT32(MF_MT_AVG_BITRATE, bitrate_), "MF_MT_AVG_BITRATE");
    Check(outputType->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Main), "MF_MT_MPEG2_PROFILE");
    Check(transform_->SetOutputType(0, outputType.Get(), 0), "SetOutputType");
    auto inputType = VideoType(MFVideoFormat_NV12, width_, height_, framesPerSecond_);
    Check(transform_->SetInputType(0, inputType.Get(), 0), "SetInputType");

    Check(MFCreateDXGIDeviceManager(&resetToken_, &deviceManager_), "MFCreateDXGIDeviceManager");
    Check(deviceManager_->ResetDevice(device, resetToken_), "ResetDevice");
    transform_->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, reinterpret_cast<ULONG_PTR>(deviceManager_.Get()));
    ICodecAPI* codecRaw = nullptr;
    if (SUCCEEDED(transform_->QueryInterface(IID_PPV_ARGS(&codecRaw))) && codecRaw != nullptr) {
        ComPtr<ICodecAPI> codec;
        codec.Attach(codecRaw);
        VARIANT value;
        VariantInit(&value);
        value.vt = VT_BOOL; value.boolVal = VARIANT_TRUE;
        codec->SetValue(&CODECAPI_AVLowLatencyMode, &value);
        VariantClear(&value);
    }
    Check(transform_->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0), "Encoder flush");
    Check(transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0), "Begin streaming");
    Check(transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0), "Start stream");
    started_ = true;
}

EncodedVideoPacket H264Encoder::Encode(ID3D11Texture2D* nv12Texture, std::int64_t timestamp100ns) {
    if (!started_ || !nv12Texture) throw std::invalid_argument("Encoder input texture is unavailable");
    ComPtr<IMFMediaBuffer> inputBuffer;
    Check(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), nv12Texture, 0, FALSE, &inputBuffer), "MFCreateDXGISurfaceBuffer");
    ComPtr<IMFSample> inputSample;
    Check(MFCreateSample(&inputSample), "MFCreateSample(input)");
    Check(inputSample->AddBuffer(inputBuffer.Get()), "AddBuffer(input)");
    Check(inputSample->SetSampleTime(timestamp100ns), "SetSampleTime");
    Check(inputSample->SetSampleDuration(10'000'000LL / framesPerSecond_), "SetSampleDuration");
    Check(transform_->ProcessInput(0, inputSample.Get(), 0), "ProcessInput");

    MFT_OUTPUT_STREAM_INFO streamInfo{};
    Check(transform_->GetOutputStreamInfo(0, &streamInfo), "GetOutputStreamInfo");
    ComPtr<IMFSample> outputSample;
    Check(MFCreateSample(&outputSample), "MFCreateSample(output)");
    ComPtr<IMFMediaBuffer> outputBuffer;
    Check(MFCreateMemoryBuffer(streamInfo.cbSize > 0 ? streamInfo.cbSize : width_ * height_, &outputBuffer), "MFCreateMemoryBuffer");
    Check(outputSample->AddBuffer(outputBuffer.Get()), "AddBuffer(output)");
    MFT_OUTPUT_DATA_BUFFER output{};
    output.dwStreamID = 0;
    output.pSample = outputSample.Get();
    DWORD status = 0;
    const HRESULT processResult = transform_->ProcessOutput(0, 1, &output, &status);
    if (output.pEvents) output.pEvents->Release();
    if (processResult == MF_E_TRANSFORM_NEED_MORE_INPUT) return {};
    Check(processResult, "ProcessOutput");

    ComPtr<IMFMediaBuffer> contiguous;
    Check(outputSample->ConvertToContiguousBuffer(&contiguous), "ConvertToContiguousBuffer");
    BYTE* data = nullptr;
    DWORD currentLength = 0;
    Check(contiguous->Lock(&data, nullptr, &currentLength), "Lock output");
    EncodedVideoPacket packet;
    packet.bytes.assign(data, data + currentLength);
    packet.timestamp100ns = timestamp100ns;
    UINT32 cleanPoint = FALSE;
    packet.cleanPoint = SUCCEEDED(outputSample->GetUINT32(MFSampleExtension_CleanPoint, &cleanPoint)) && cleanPoint != FALSE;
    contiguous->Unlock();
    return packet;
}
