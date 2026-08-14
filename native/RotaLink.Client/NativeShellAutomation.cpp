#include "NativeShellAutomation.h"
#include <UIAutomationClient.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>

using Microsoft::WRL::ComPtr;

namespace {
class ComApartment final {
public:
    ComApartment() noexcept : result_(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)) {}
    ~ComApartment() { if (SUCCEEDED(result_)) CoUninitialize(); }
    [[nodiscard]] bool Available() const noexcept { return SUCCEEDED(result_) || result_ == RPC_E_CHANGED_MODE; }
    [[nodiscard]] HRESULT Result() const noexcept { return result_; }
private:
    HRESULT result_{};
};

std::wstring WindowClass(HWND window) {
    wchar_t value[256]{};
    if (!window || GetClassNameW(window, value, static_cast<int>(_countof(value))) <= 0) return {};
    return value;
}

std::wstring ElementName(IUIAutomationElement* element) {
    if (!element) return {};
    BSTR value = nullptr;
    if (FAILED(element->get_CurrentName(&value)) || !value) return {};
    std::wstring result(value, SysStringLen(value));
    SysFreeString(value);
    return result;
}

template<typename T>
HRESULT Pattern(IUIAutomationElement* element, PATTERNID id, ComPtr<T>& result) {
    ComPtr<IUnknown> unknown;
    const HRESULT query = element->GetCurrentPattern(id, unknown.GetAddressOf());
    if (FAILED(query) || !unknown) return FAILED(query) ? query : E_NOINTERFACE;
    return unknown.As(&result);
}

HRESULT InvokeElement(IUIAutomation* automation, IUIAutomationElement* start,
    bool searchParents, std::wstring& name) {
    if (!automation || !start) return E_POINTER;
    ComPtr<IUIAutomationTreeWalker> walker;
    if (searchParents) {
        const HRESULT walkerResult = automation->get_ControlViewWalker(walker.GetAddressOf());
        if (FAILED(walkerResult)) return walkerResult;
    }

    ComPtr<IUIAutomationElement> current;
    current = start;
    HRESULT last = UIA_E_NOTSUPPORTED;
    for (int depth = 0; current && depth < (searchParents ? 7 : 1); ++depth) {
        ComPtr<IUIAutomationInvokePattern> invoke;
        last = Pattern(current.Get(), UIA_InvokePatternId, invoke);
        if (SUCCEEDED(last) && invoke) {
            name = ElementName(current.Get());
            return invoke->Invoke();
        }

        ComPtr<IUIAutomationLegacyIAccessiblePattern> legacy;
        last = Pattern(current.Get(), UIA_LegacyIAccessiblePatternId, legacy);
        if (SUCCEEDED(last) && legacy) {
            name = ElementName(current.Get());
            return legacy->DoDefaultAction();
        }

        if (!searchParents) break;
        ComPtr<IUIAutomationElement> parent;
        last = walker->GetParentElement(current.Get(), parent.GetAddressOf());
        if (FAILED(last) || !parent) break;
        current = std::move(parent);
    }
    return FAILED(last) ? last : UIA_E_NOTSUPPORTED;
}

DWORD ErrorCode(HRESULT result) noexcept { return static_cast<DWORD>(result); }
}

ShellAutomationResult NativeShellAutomation::TryHandleLeftClick(POINT point) noexcept {
    ShellAutomationResult result;
    const HWND hit = WindowFromPoint(point);
    result.targetClass = WindowClass(hit);
    const HWND root = hit ? GetAncestor(hit, GA_ROOT) : nullptr;
    const std::wstring rootClass = WindowClass(root);
    const bool desktop = _wcsicmp(result.targetClass.c_str(), L"SysListView32") == 0;
    const bool taskbar = _wcsicmp(rootClass.c_str(), L"Shell_TrayWnd") == 0 ||
        _wcsicmp(result.targetClass.c_str(), L"MSTaskListWClass") == 0;
    if (!desktop && !taskbar) return result;

    ComApartment apartment;
    if (!apartment.Available()) {
        result.status = ShellAutomationStatus::Failed;
        result.error = ErrorCode(apartment.Result());
        result.stage = "shell-com-initialize-failed";
        return result;
    }

    ComPtr<IUIAutomation> automation;
    HRESULT operation = CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(automation.GetAddressOf()));
    if (FAILED(operation) || !automation) {
        result.status = ShellAutomationStatus::Failed;
        result.error = ErrorCode(operation);
        result.stage = "shell-automation-create-failed";
        return result;
    }

    ComPtr<IUIAutomationElement> element;
    operation = automation->ElementFromPoint(point, element.GetAddressOf());
    if (FAILED(operation) || !element) {
        result.status = ShellAutomationStatus::Failed;
        result.error = ErrorCode(operation);
        result.stage = "shell-element-from-point-failed";
        return result;
    }
    result.targetName = ElementName(element.Get());

    if (taskbar) {
        operation = InvokeElement(automation.Get(), element.Get(), true, result.targetName);
        result.status = SUCCEEDED(operation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
        result.error = SUCCEEDED(operation) ? ERROR_SUCCESS : ErrorCode(operation);
        result.stage = SUCCEEDED(operation) ? "native-taskbar-invoke-ok" : "native-taskbar-pattern-unavailable";
        return result;
    }

    ComPtr<IUIAutomationSelectionItemPattern> selection;
    operation = Pattern(element.Get(), UIA_SelectionItemPatternId, selection);
    if (FAILED(operation) || !selection) {
        // Empty desktop space is intentionally left to the physical input path.
        result.status = ShellAutomationStatus::NotShell;
        result.stage = "desktop-empty-space";
        return result;
    }

    const DWORD now = GetTickCount();
    const DWORD elapsed = now - lastDesktopClickTick_;
    const int maximumX = (std::max)(GetSystemMetrics(SM_CXDOUBLECLK), 4);
    const int maximumY = (std::max)(GetSystemMetrics(SM_CYDOUBLECLK), 4);
    const bool doubleClick = lastDesktopWindow_ == hit && elapsed <= GetDoubleClickTime() &&
        std::abs(point.x - lastDesktopPoint_.x) <= maximumX &&
        std::abs(point.y - lastDesktopPoint_.y) <= maximumY;
    lastDesktopClickTick_ = now;
    lastDesktopPoint_ = point;
    lastDesktopWindow_ = hit;

    if (doubleClick) {
        operation = InvokeElement(automation.Get(), element.Get(), false, result.targetName);
        result.status = SUCCEEDED(operation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
        result.error = SUCCEEDED(operation) ? ERROR_SUCCESS : ErrorCode(operation);
        result.stage = SUCCEEDED(operation) ? "native-desktop-invoke-ok" : "native-desktop-invoke-unavailable";
        lastDesktopClickTick_ = 0;
        lastDesktopWindow_ = nullptr;
        return result;
    }

    operation = selection->Select();
    if (SUCCEEDED(operation)) operation = element->SetFocus();
    result.status = SUCCEEDED(operation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
    result.error = SUCCEEDED(operation) ? ERROR_SUCCESS : ErrorCode(operation);
    result.stage = SUCCEEDED(operation) ? "native-desktop-selection-ok" : "native-desktop-selection-failed";
    return result;
}
