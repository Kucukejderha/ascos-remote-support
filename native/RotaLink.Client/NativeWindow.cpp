#include "NativeWindow.h"
#include "Diagnostics.h"
#include <windowsx.h>
#include <memory>
#include <utility>

namespace {
constexpr wchar_t WindowClass[] = L"Rotaniz.RotaLink.Native.Window";
constexpr wchar_t WindowTitle[] = L"Rotaniz Remote Support — RotaLink";
constexpr UINT StatusMessage = WM_APP + 1;
constexpr UINT SessionReadyMessage = WM_APP + 2;
constexpr UINT DpiChangedMessage = 0x02E0;
constexpr COLORREF Navy = RGB(7, 27, 43);
constexpr COLORREF Blue = RGB(11, 102, 195);

struct StatusPayload final { std::wstring text; COLORREF color{}; };
struct SessionPayload final { std::wstring code; };

int Scale(int value, unsigned dpi) { return MulDiv(value, static_cast<int>(dpi), 96); }

unsigned WindowDpi(HWND window) noexcept {
    using GetDpiForWindowFunction = UINT(WINAPI*)(HWND);
    const HMODULE user32 = GetModuleHandleW(L"user32.dll");
    const auto function = user32
        ? reinterpret_cast<GetDpiForWindowFunction>(GetProcAddress(user32, "GetDpiForWindow")) : nullptr;
    if (function) return function(window);
    HDC dc = GetDC(window);
    if (!dc) return 96;
    const int dpi = GetDeviceCaps(dc, LOGPIXELSX);
    ReleaseDC(window, dc);
    return dpi > 0 ? static_cast<unsigned>(dpi) : 96;
}

HFONT CreateUiFont(int points, int weight, unsigned dpi) {
    return CreateFontW(-MulDiv(points, static_cast<int>(dpi), 72), 0, 0, 0, weight, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
}

void Fill(HDC dc, const RECT& rectangle, COLORREF color) {
    HBRUSH brush = CreateSolidBrush(color);
    FillRect(dc, &rectangle, brush);
    DeleteObject(brush);
}

void Text(HDC dc, const wchar_t* value, RECT rectangle, HFONT font, COLORREF color, UINT format) {
    const HGDIOBJ previous = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, color);
    DrawTextW(dc, value, -1, &rectangle, format);
    SelectObject(dc, previous);
}
}

NativeWindow::NativeWindow(PlatformCompatibility compatibility) : compatibility_(std::move(compatibility)) {}

NativeWindow::~NativeWindow() {
    closing_.store(true);
    if (sessionRuntime_) sessionRuntime_->Stop();
    if (nativeRuntime_) nativeRuntime_->Stop();
    if (titleFont_) DeleteObject(titleFont_);
    if (bodyFont_) DeleteObject(bodyFont_);
    if (codeFont_) DeleteObject(codeFont_);
}

bool NativeWindow::Create(HINSTANCE instance, int showCommand) {
    WNDCLASSEXW windowClass{sizeof(windowClass)};
    windowClass.style = CS_HREDRAW | CS_VREDRAW;
    windowClass.lpfnWndProc = WindowProcedure;
    windowClass.hInstance = instance;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1));
    windowClass.hIconSm = windowClass.hIcon;
    windowClass.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    windowClass.lpszClassName = WindowClass;
    if (!RegisterClassExW(&windowClass) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
    window_ = CreateWindowExW(0, WindowClass, WindowTitle, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        CW_USEDEFAULT, CW_USEDEFAULT, Scale(560, dpi_), Scale(380, dpi_), nullptr, nullptr, instance, this);
    if (!window_) return false;
    ShowWindow(window_, showCommand);
    UpdateWindow(window_);
    StartSession();
    return true;
}

int NativeWindow::Run() {
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return static_cast<int>(message.wParam);
}

LRESULT CALLBACK NativeWindow::WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    NativeWindow* self = reinterpret_cast<NativeWindow*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        self = static_cast<NativeWindow*>(create->lpCreateParams);
        self->window_ = window;
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    return self ? self->HandleMessage(message, wParam, lParam) : DefWindowProcW(window, message, wParam, lParam);
}

LRESULT NativeWindow::HandleMessage(UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
    case WM_CREATE:
        dpi_ = WindowDpi(window_);
        RecreateFonts(dpi_);
        return 0;
    case DpiChangedMessage: {
        dpi_ = HIWORD(wParam);
        RecreateFonts(dpi_);
        const auto suggested = reinterpret_cast<RECT*>(lParam);
        SetWindowPos(window_, nullptr, suggested->left, suggested->top, suggested->right - suggested->left,
            suggested->bottom - suggested->top, SWP_NOACTIVATE | SWP_NOZORDER);
        return 0;
    }
    case StatusMessage: {
        std::unique_ptr<StatusPayload> payload(reinterpret_cast<StatusPayload*>(lParam));
        if (payload) SetStatus(std::move(payload->text), payload->color);
        return 0;
    }
    case SessionReadyMessage: {
        std::unique_ptr<SessionPayload> payload(reinterpret_cast<SessionPayload*>(lParam));
        if (payload) {
            code_ = std::move(payload->code);
            InvalidateRect(window_, nullptr, FALSE);
        }
        return 0;
    }
    case WM_PAINT: Paint(); return 0;
    case WM_CLOSE: closing_.store(true); DestroyWindow(window_); return 0;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    default: return DefWindowProcW(window_, message, wParam, lParam);
    }
}

void NativeWindow::Paint() const {
    PAINTSTRUCT paint{};
    HDC dc = BeginPaint(window_, &paint);
    RECT client{};
    GetClientRect(window_, &client);
    Fill(dc, client, RGB(244, 247, 250));
    RECT header{0, 0, client.right, Scale(104, dpi_)};
    Fill(dc, header, Navy);
    Text(dc, L"RotaLink", {Scale(28,dpi_),Scale(16,dpi_),client.right-Scale(20,dpi_),Scale(62,dpi_)},
        titleFont_, RGB(255,255,255), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    Text(dc, L"Rotaniz Remote Support • Native Win32", {Scale(31,dpi_),Scale(62,dpi_),client.right-Scale(20,dpi_),Scale(92,dpi_)},
        bodyFont_, RGB(92,214,164), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    RECT card{Scale(28,dpi_),Scale(130,dpi_),client.right-Scale(28,dpi_),Scale(326,dpi_)};
    Fill(dc, card, RGB(255,255,255));
    FrameRect(dc, &card, reinterpret_cast<HBRUSH>(GetStockObject(LTGRAY_BRUSH)));
    Text(dc, L"DESTEK KODUNUZ", {card.left+Scale(24,dpi_),card.top+Scale(18,dpi_),card.right-Scale(20,dpi_),card.top+Scale(46,dpi_)},
        bodyFont_, Blue, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    Text(dc, code_.c_str(), {card.left+Scale(22,dpi_),card.top+Scale(45,dpi_),card.right-Scale(20,dpi_),card.top+Scale(102,dpi_)},
        codeFont_, Navy, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    Text(dc, status_.c_str(), {card.left+Scale(24,dpi_),card.top+Scale(112,dpi_),card.right-Scale(20,dpi_),card.top+Scale(142,dpi_)},
        bodyFont_, statusColor_, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS | DT_VCENTER);
    Text(dc, L"Bu sürüm müşteri bilgisayarında .NET veya VC++ Runtime gerektirmez.",
        {card.left+Scale(24,dpi_),card.top+Scale(148,dpi_),card.right-Scale(20,dpi_),card.bottom-Scale(12,dpi_)},
        bodyFont_, RGB(70,96,117), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    Text(dc, L"v1.2.0-native.9", {Scale(350,dpi_),client.bottom-Scale(42,dpi_),client.right-Scale(28,dpi_),client.bottom-Scale(8,dpi_)},
        bodyFont_, RGB(99,120,138), DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
    EndPaint(window_, &paint);
}

void NativeWindow::StartSession() {
    try {
        nativeRuntime_ = std::make_unique<NativeRuntime>();
        nativeRuntime_->StartForCurrentClient();
    } catch (const std::exception& error) {
        const std::string narrow(error.what());
        const std::wstring message(narrow.begin(), narrow.end());
        Diagnostics::Write(L"Native control runtime startup failed: " + message);
        SetStatus(L"Kontrol motoru başlatılamadı: " + message, RGB(178,34,34));
        return;
    }
    sessionRuntime_ = std::make_unique<SessionRuntime>(
        [this](const NativeHostSession& session) { PostSessionReady(session); },
        [this](std::wstring status, bool error) { PostStatus(std::move(status), error); });
    sessionRuntime_->Start();
}

void NativeWindow::PostSessionReady(const NativeHostSession& session) {
    if (closing_.load()) return;
    auto payload = std::make_unique<SessionPayload>();
    payload->code.assign(session.code.begin(), session.code.end());
    if (PostMessageW(window_, SessionReadyMessage, 0, reinterpret_cast<LPARAM>(payload.get()))) payload.release();
}

void NativeWindow::PostStatus(std::wstring status, bool error) {
    if (closing_.load()) return;
    auto payload = std::make_unique<StatusPayload>();
    payload->text = std::move(status);
    payload->color = error ? RGB(178,34,34) : Blue;
    if (PostMessageW(window_, StatusMessage, 0, reinterpret_cast<LPARAM>(payload.get()))) payload.release();
}

void NativeWindow::SetStatus(std::wstring status, COLORREF color) {
    status_ = std::move(status);
    statusColor_ = color;
    InvalidateRect(window_, nullptr, FALSE);
}

void NativeWindow::RecreateFonts(unsigned dpi) {
    if (titleFont_) DeleteObject(titleFont_);
    if (bodyFont_) DeleteObject(bodyFont_);
    if (codeFont_) DeleteObject(codeFont_);
    titleFont_ = CreateUiFont(25, FW_BOLD, dpi);
    bodyFont_ = CreateUiFont(10, FW_NORMAL, dpi);
    codeFont_ = CreateUiFont(28, FW_BOLD, dpi);
}
