cmake_minimum_required(VERSION 3.24)
cmake_policy(SET CMP0091 NEW)
project(RotaLinkNative LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
  # The customer executable must not require the Visual C++ Redistributable.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_executable(RotaLink.NativeCapture
  RotaLink.NativeHost/DesktopDuplicator.cpp
  RotaLink.NativeHost/GpuColorConverter.cpp
  RotaLink.NativeHost/H264Encoder.cpp
  RotaLink.NativeHost/SharedFrameBuffer.cpp
  RotaLink.NativeHost/main.cpp)
target_compile_definitions(RotaLink.NativeCapture PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(RotaLink.NativeCapture PRIVATE d3d11 dxgi ole32 oleaut32 mf mfplat mfuuid)
if(MSVC)
  target_compile_options(RotaLink.NativeCapture PRIVATE /W4 /WX /permissive- /EHsc)
endif()

set(ROTALINK_ICON "${CMAKE_CURRENT_SOURCE_DIR}/../client/RemoteSupport.SessionAgent/Assets/RotaLink.ico")
set(ROTALINK_MANIFEST "${CMAKE_CURRENT_SOURCE_DIR}/RotaLink.Client/app.manifest")
configure_file(RotaLink.Client/resources.rc.in "${CMAKE_CURRENT_BINARY_DIR}/RotaLink.Client.rc" @ONLY)

add_executable(RotaLink.Client WIN32
  RotaLink.Client/CngDeviceIdentity.cpp
  RotaLink.Client/Diagnostics.cpp
  RotaLink.Client/GdiJpegCapture.cpp
  RotaLink.Client/InputPipe.cpp
  RotaLink.Client/JsonLite.cpp
  RotaLink.Client/NativeInputEngine.cpp
  RotaLink.Client/NativeRuntime.cpp
  RotaLink.Client/PlatformCompatibility.cpp
  RotaLink.Client/SessionRuntime.cpp
  RotaLink.Client/SignalingClient.cpp
  RotaLink.Client/WinHttpHealthClient.cpp
  RotaLink.Client/NativeWindow.cpp
  RotaLink.Client/main.cpp
  RotaLink.NativeHost/DesktopDuplicator.cpp
  RotaLink.NativeHost/GpuColorConverter.cpp
  RotaLink.NativeHost/H264Encoder.cpp
  "${CMAKE_CURRENT_BINARY_DIR}/RotaLink.Client.rc")
target_include_directories(RotaLink.Client PRIVATE RotaLink.Client RotaLink.NativeHost)
target_compile_definitions(RotaLink.Client PRIVATE
  UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX
  WINVER=0x0602 _WIN32_WINNT=0x0602 NTDDI_VERSION=0x06020000)
target_link_libraries(RotaLink.Client PRIVATE advapi32 bcrypt crypt32 d3d11 dxgi gdi32 mf mfplat mfuuid
  ole32 oleaut32 shell32 user32 userenv windowscodecs winhttp wtsapi32)
set_target_properties(RotaLink.Client PROPERTIES OUTPUT_NAME "RotaLink")
if(MSVC)
  target_compile_options(RotaLink.Client PRIVATE /W4 /WX /permissive- /EHsc /utf-8 /guard:cf
    $<$<CONFIG:Release>:/O1> $<$<CONFIG:Release>:/GL> $<$<CONFIG:Release>:/Gy>
    $<$<CONFIG:Release>:/Gw> $<$<CONFIG:Release>:/Zc:inline>)
  target_link_options(RotaLink.Client PRIVATE /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA /CETCOMPAT
    $<$<CONFIG:Release>:/LTCG> $<$<CONFIG:Release>:/OPT:REF>
    $<$<CONFIG:Release>:/OPT:ICF> $<$<CONFIG:Release>:/DEBUG:NONE>)
endif()
