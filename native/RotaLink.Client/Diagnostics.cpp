#include "Diagnostics.h"
#include "NativeHandles.h"
#include <windows.h>
#include <array>
#include <filesystem>
#include <mutex>
#include <vector>

namespace {
std::mutex logMutex;
std::wstring logPath;

std::wstring ExecutableDirectory() {
    std::vector<wchar_t> buffer(4096);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return L".";
    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path().wstring();
}

std::wstring ResolveLogPath(const std::wstring& directory) {
    const std::filesystem::path base = directory.empty() ? ExecutableDirectory() : directory;
    return (base / L"RotaLink-Native.log").wstring();
}

std::wstring Timestamp() {
    SYSTEMTIME value{};
    GetLocalTime(&value);
    wchar_t buffer[64]{};
    swprintf_s(buffer, L"%04u-%02u-%02u %02u:%02u:%02u.%03u", value.wYear, value.wMonth,
        value.wDay, value.wHour, value.wMinute, value.wSecond, value.wMilliseconds);
    return buffer;
}
}

bool Diagnostics::Initialize(const std::wstring& directory) noexcept {
    std::scoped_lock lock(logMutex);
    logPath = ResolveLogPath(directory);
    UniqueHandle file(CreateFileW(logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    return static_cast<bool>(file);
}

void Diagnostics::Write(const std::wstring& message) noexcept {
    try {
        std::scoped_lock lock(logMutex);
        if (logPath.empty()) logPath = ResolveLogPath({});
        UniqueHandle file(CreateFileW(logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
        if (!file) return;
        const std::wstring line = Timestamp() + L" " + message + L"\r\n";
        const int byteCount = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()),
            nullptr, 0, nullptr, nullptr);
        if (byteCount <= 0) return;
        std::string utf8(static_cast<std::size_t>(byteCount), '\0');
        WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()),
            utf8.data(), byteCount, nullptr, nullptr);
        DWORD written = 0;
        WriteFile(file.get(), utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    } catch (...) { }
}

std::wstring Diagnostics::LogPath() {
    std::scoped_lock lock(logMutex);
    if (logPath.empty()) logPath = ResolveLogPath({});
    return logPath;
}
