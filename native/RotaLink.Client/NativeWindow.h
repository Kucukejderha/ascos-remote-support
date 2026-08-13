#pragma once

#include "PlatformCompatibility.h"
#include <windows.h>
#include <atomic>
#include <string>
#include <thread>

class NativeWindow final {
public:
    explicit NativeWindow(PlatformCompatibility compatibility);
    ~NativeWindow();
    NativeWindow(const NativeWindow&) = delete;
    NativeWindow& operator=(const NativeWindow&) = delete;
    bool Create(HINSTANCE instance, int showCommand);
    int Run();
private:
    static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    void Paint() const;
    void StartHealthProbe();
    void SetStatus(std::wstring status, COLORREF color);
    void RecreateFonts(unsigned dpi);
    PlatformCompatibility compatibility_;
    HWND window_{};
    HFONT titleFont_{};
    HFONT bodyFont_{};
    HFONT codeFont_{};
    std::thread healthThread_;
    std::atomic_bool closing_{};
    std::wstring status_{L"Native bağlantı denetleniyor…"};
    COLORREF statusColor_{RGB(99, 120, 138)};
    unsigned dpi_{96};
};
