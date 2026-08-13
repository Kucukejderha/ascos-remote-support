#include "Diagnostics.h"
#include "NativeHandles.h"
#include "NativeWindow.h"
#include "NativeRuntime.h"
#include "PlatformCompatibility.h"
#include <windows.h>
#include <shellapi.h>
#include <string_view>

namespace {
constexpr wchar_t MutexName[] = L"Local\\Rotaniz.RotaLink.Client";
constexpr wchar_t WindowTitle[] = L"Rotaniz Remote Support — RotaLink";

void EnableDpiAwareness() noexcept {
    using SetContext = BOOL(WINAPI*)(HANDLE);
    const HMODULE user32 = GetModuleHandleW(L"user32.dll");
    const auto setContext = user32
        ? reinterpret_cast<SetContext>(GetProcAddress(user32, "SetProcessDpiAwarenessContext")) : nullptr;
    if (setContext && setContext(reinterpret_cast<HANDLE>(-4))) return; // Per-monitor v2 on supported Windows.
    SetProcessDPIAware();
}

bool ActivateExistingWindow() noexcept {
    const HWND existing = FindWindowW(nullptr, WindowTitle);
    if (!existing) return false;
    ShowWindowAsync(existing, SW_RESTORE);
    SetForegroundWindow(existing);
    return true;
}

bool IsProcessElevated() noexcept {
    HANDLE rawToken = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &rawToken)) return false;
    UniqueHandle token(rawToken);
    TOKEN_ELEVATION elevation{};
    DWORD bytes = 0;
    return GetTokenInformation(token.get(), TokenElevation, &elevation, sizeof(elevation), &bytes) != FALSE &&
        elevation.TokenIsElevated != 0;
}

bool HasArgument(std::wstring_view commandLine, std::wstring_view argument) noexcept {
    const std::size_t position = commandLine.find(argument);
    if (position == std::wstring_view::npos) return false;
    const bool leftBoundary = position == 0 || commandLine[position - 1] == L' ' || commandLine[position - 1] == L'\t';
    const std::size_t end = position + argument.size();
    const bool rightBoundary = end == commandLine.size() || commandLine[end] == L' ' || commandLine[end] == L'\t';
    return leftBoundary && rightBoundary;
}

DWORD ProcessIdArgument(std::wstring_view commandLine) noexcept {
    constexpr std::wstring_view marker = L"--client-pid";
    const std::size_t position = commandLine.find(marker);
    if (position == std::wstring_view::npos) return 0;
    const std::size_t valueStart = commandLine.find_first_not_of(L" \t", position + marker.size());
    if (valueStart == std::wstring_view::npos) return 0;
    wchar_t* end = nullptr;
    const unsigned long value = wcstoul(commandLine.data() + valueStart, &end, 10);
    return end != commandLine.data() + valueStart ? static_cast<DWORD>(value) : 0;
}

std::wstring ArgumentValue(const wchar_t* name) noexcept {
    int count = 0;
    LPWSTR* values = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!values) return {};
    std::wstring result;
    for (int index = 1; index + 1 < count; ++index) {
        if (_wcsicmp(values[index], name) == 0) {
            result = values[index + 1];
            break;
        }
    }
    LocalFree(values);
    return result;
}

bool RelaunchElevated() noexcept {
    wchar_t executable[MAX_PATH]{};
    const DWORD length = GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable));
    if (length == 0 || length >= ARRAYSIZE(executable)) return false;
    SHELLEXECUTEINFOW request{sizeof(request)};
    request.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    request.lpVerb = L"runas";
    request.lpFile = executable;
    request.lpParameters = L"--elevated";
    request.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&request)) return false;
    if (request.hProcess) CloseHandle(request.hProcess);
    return true;
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR commandLine, int showCommand) {
    const std::wstring_view arguments = commandLine ? commandLine : L"";
    if (HasArgument(arguments, L"--service")) return NativeRuntime::RunServiceMode();
    if (HasArgument(arguments, L"--helper"))
        return NativeRuntime::RunHelperMode(ProcessIdArgument(arguments), ArgumentValue(L"--log-directory"));
    EnableDpiAwareness();
    if (!Diagnostics::Initialize()) {
        MessageBoxW(nullptr,
            L"RotaLink-Native.log dosyası uygulamanın bulunduğu klasörde oluşturulamadı.\n\n"
            L"RotaLink.exe dosyasını yazma izniniz olan bir klasöre taşıyıp yeniden çalıştırın.",
            L"RotaLink tanılama dosyası", MB_OK | MB_ICONERROR);
        return 6;
    }
    // A second launch must focus the already running elevated window without
    // presenting another UAC prompt.
    if (ActivateExistingWindow()) return 0;
    const bool elevatedMarker = HasArgument(arguments, L"--elevated");
    if (!IsProcessElevated()) {
        if (!elevatedMarker && RelaunchElevated()) return 0;
        const DWORD error = GetLastError();
        Diagnostics::Write(L"Elevation was not granted. Win32=" + std::to_wstring(error));
        MessageBoxW(nullptr,
            L"Uzak kontrol motorunu başlatmak için Windows yönetici onayı gereklidir.\n\n"
            L"Hiçbir ek .NET veya çalışma zamanı kurulmayacaktır.",
            L"RotaLink yönetici onayı", MB_OK | MB_ICONWARNING);
        return 5;
    }
    UniqueHandle mutex(CreateMutexW(nullptr, FALSE, MutexName));
    if (!mutex) {
        MessageBoxW(nullptr, L"Tek örnek kilidi oluşturulamadı.", L"RotaLink", MB_OK | MB_ICONERROR);
        return 2;
    }
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        ActivateExistingWindow();
        return 0;
    }
    const PlatformCompatibility compatibility = PlatformCompatibility::Evaluate();
    Diagnostics::Write(L"RotaLink v1.2.0-native.5 started. Log=" + Diagnostics::LogPath() + L". " + compatibility.DiagnosticText());
    if (!compatibility.supported) {
        MessageBoxW(nullptr, compatibility.reason.c_str(), L"RotaLink uyumluluk denetimi", MB_OK | MB_ICONERROR);
        return 3;
    }
    NativeWindow window(compatibility);
    if (!window.Create(instance, showCommand)) {
        Diagnostics::Write(L"Native window creation failed. Win32=" + std::to_wstring(GetLastError()));
        return 4;
    }
    return window.Run();
}
