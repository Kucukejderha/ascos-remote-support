#include "Diagnostics.h"
#include "NativeHandles.h"
#include "NativeWindow.h"
#include "PlatformCompatibility.h"
#include <windows.h>

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

void ActivateExistingWindow() noexcept {
    const HWND existing = FindWindowW(nullptr, WindowTitle);
    if (!existing) return;
    ShowWindowAsync(existing, SW_RESTORE);
    SetForegroundWindow(existing);
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
    EnableDpiAwareness();
    Diagnostics::Initialize();
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
    Diagnostics::Write(L"RotaLink v1.2.0-native.1 started. " + compatibility.DiagnosticText());
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
