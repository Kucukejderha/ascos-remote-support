#include "PlatformCompatibility.h"
#include "NativeHandles.h"
#include <windows.h>
#include <winternl.h>
#include <sstream>

namespace {
std::wstring ReadString(HKEY root, const wchar_t* path, const wchar_t* name) {
    HKEY raw = nullptr;
    if (RegOpenKeyExW(root, path, 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &raw) != ERROR_SUCCESS) return {};
    UniqueRegistryKey key(raw);
    DWORD type = 0;
    DWORD bytes = 0;
    if (RegQueryValueExW(key.get(), name, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS ||
        (type != REG_SZ && type != REG_EXPAND_SZ) || bytes < sizeof(wchar_t)) return {};
    std::wstring result(bytes / sizeof(wchar_t), L'\0');
    if (RegQueryValueExW(key.get(), name, nullptr, &type,
        reinterpret_cast<BYTE*>(result.data()), &bytes) != ERROR_SUCCESS) return {};
    while (!result.empty() && result.back() == L'\0') result.pop_back();
    return result;
}

bool ContainsIgnoreCase(const std::wstring& text, const std::wstring& value) {
    if (value.empty() || value.size() > text.size()) return false;
    for (std::size_t offset = 0; offset + value.size() <= text.size(); ++offset) {
        if (_wcsnicmp(text.c_str() + offset, value.c_str(), value.size()) == 0) return true;
    }
    return false;
}
}

PlatformCompatibility PlatformCompatibility::Evaluate() {
    PlatformCompatibility result;
#if !defined(_M_X64)
    result.reason = L"RotaLink yalnızca x64 Windows sistemlerini destekler.";
    return result;
#endif
    using RtlGetVersionFunction = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    const auto rtlGetVersion = ntdll
        ? reinterpret_cast<RtlGetVersionFunction>(GetProcAddress(ntdll, "RtlGetVersion")) : nullptr;
    RTL_OSVERSIONINFOW version{};
    version.dwOSVersionInfoSize = sizeof(version);
    if (!rtlGetVersion || rtlGetVersion(&version) != 0) {
        result.reason = L"Windows sürümü güvenilir biçimde belirlenemedi.";
        return result;
    }
    result.major = version.dwMajorVersion;
    result.minor = version.dwMinorVersion;
    result.build = version.dwBuildNumber;
    constexpr wchar_t currentVersion[] = L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";
    result.productName = ReadString(HKEY_LOCAL_MACHINE, currentVersion, L"ProductName");
    result.installationType = ReadString(HKEY_LOCAL_MACHINE, currentVersion, L"InstallationType");
    result.server = ContainsIgnoreCase(result.installationType, L"Server") ||
        ContainsIgnoreCase(result.productName, L"Server");
    result.serverCore = ContainsIgnoreCase(result.installationType, L"Core");
    if (result.serverCore) {
        result.reason = L"Server Core etkileşimli uzak destek hedefi değildir; Desktop Experience gereklidir.";
        return result;
    }
    const bool versionSupported = result.server
        ? (result.major > 6 || (result.major == 6 && result.minor >= 2))
        : result.major >= 10;
    if (!versionSupported) {
        result.reason = L"Bu Windows sürümü destek matrisinin dışındadır.";
        return result;
    }
    result.supported = true;
    result.reason = L"Native Win32 çalışma zamanı hazır; .NET gerekmiyor.";
    return result;
}

std::wstring PlatformCompatibility::DiagnosticText() const {
    std::wostringstream text;
    text << L"Platform=" << productName << L", Version=" << major << L'.' << minor << L'.' << build
         << L", InstallationType=" << installationType << L", Architecture=x64, Runtime=native-win32, Supported="
         << (supported ? L"True" : L"False") << L", Reason=" << reason;
    return text.str();
}
