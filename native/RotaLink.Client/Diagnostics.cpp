#include "Diagnostics.h"
#include "NativeHandles.h"
#include <shlobj.h>
#include <windows.h>
#include <array>
#include <mutex>

namespace {
std::mutex logMutex;
std::wstring logPath;

std::wstring ResolveLogPath() {
    PWSTR localAppData = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_CREATE, nullptr, &localAppData)))
        return L"RotaLink-Native.log";
    std::wstring directory(localAppData);
    CoTaskMemFree(localAppData);
    directory += L"\\Rotaniz\\RotaLink";
    CreateDirectoryW((directory.substr(0, directory.find_last_of(L'\\'))).c_str(), nullptr);
    CreateDirectoryW(directory.c_str(), nullptr);
    return directory + L"\\RotaLink-Native.log";
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

void Diagnostics::Initialize() {
    std::scoped_lock lock(logMutex);
    if (logPath.empty()) logPath = ResolveLogPath();
}

void Diagnostics::Write(const std::wstring& message) noexcept {
    try {
        std::scoped_lock lock(logMutex);
        if (logPath.empty()) logPath = ResolveLogPath();
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
    if (logPath.empty()) logPath = ResolveLogPath();
    return logPath;
}
