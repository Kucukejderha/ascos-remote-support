#include "NativeShellAutomation.h"
#include <ole2.h>
#include <oleacc.h>
#include <oleauto.h>
#include <UIAutomation.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <limits>

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

class AccessibleTarget final {
public:
    AccessibleTarget() noexcept { VariantInit(&child_); }
    ~AccessibleTarget() { VariantClear(&child_); }
    AccessibleTarget(const AccessibleTarget&) = delete;
    AccessibleTarget& operator=(const AccessibleTarget&) = delete;

    HRESULT FromPoint(POINT point) noexcept {
        IAccessible* raw = nullptr;
        const HRESULT found = AccessibleObjectFromPoint(point, &raw, &child_);
        if (FAILED(found)) return found;
        if (!raw) return E_NOINTERFACE;
        accessible_.Attach(raw);

        // Walk accessibility providers that initially expose only a container.
        // Explorer 2012/2016/2019 frequently needs an explicit accHitTest before
        // returning the desktop icon, taskbar button or popup-menu item.
        for (int depth = 0; depth < 8; ++depth) {
            if (child_.vt == VT_DISPATCH && child_.pdispVal) {
                ComPtr<IAccessible> actual;
                const HRESULT converted = child_.pdispVal->QueryInterface(
                    IID_PPV_ARGS(actual.GetAddressOf()));
                if (FAILED(converted) || !actual) return FAILED(converted) ? converted : E_NOINTERFACE;
                VariantClear(&child_);
                VariantInit(&child_);
                child_.vt = VT_I4;
                child_.lVal = CHILDID_SELF;
                accessible_ = std::move(actual);
            }
            if (child_.vt != VT_I4) return E_INVALIDARG;
            if (child_.lVal != CHILDID_SELF) return S_OK;

            VARIANT nested{};
            VariantInit(&nested);
            const HRESULT tested = accessible_->accHitTest(point.x, point.y, &nested);
            if (FAILED(tested)) {
                VariantClear(&nested);
                return tested;
            }
            const bool unchanged = nested.vt == VT_EMPTY ||
                (nested.vt == VT_I4 && nested.lVal == CHILDID_SELF);
            if (unchanged) {
                VariantClear(&nested);
                return S_OK;
            }
            VariantClear(&child_);
            VariantInit(&child_);
            const HRESULT copied = VariantCopy(&child_, &nested);
            VariantClear(&nested);
            if (FAILED(copied)) return copied;
        }
        return child_.vt == VT_I4 ? S_OK : E_INVALIDARG;
    }

    [[nodiscard]] bool Available() const noexcept { return accessible_ != nullptr; }

    HRESULT Role(LONG& role) const noexcept {
        if (!accessible_) return E_POINTER;
        VARIANT value{};
        VariantInit(&value);
        const HRESULT queried = accessible_->get_accRole(child_, &value);
        const bool valid = SUCCEEDED(queried) && value.vt == VT_I4;
        if (valid) role = value.lVal;
        VariantClear(&value);
        return valid ? queried : (FAILED(queried) ? queried : DISP_E_TYPEMISMATCH);
    }

    std::wstring Name() const noexcept {
        if (!accessible_) return {};
        BSTR value = nullptr;
        if (FAILED(accessible_->get_accName(child_, &value)) || !value) return {};
        std::wstring result(value, SysStringLen(value));
        SysFreeString(value);
        return result;
    }

    HRESULT Select() const noexcept {
        if (!accessible_) return E_POINTER;
        HRESULT selected = accessible_->accSelect(
            SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION, child_);
        if (SUCCEEDED(selected)) return selected;

        // A few Server-era Explorer providers reject combined flags although
        // they accept the same operations separately.
        selected = accessible_->accSelect(SELFLAG_TAKEFOCUS, child_);
        if (FAILED(selected)) return selected;
        return accessible_->accSelect(SELFLAG_TAKESELECTION, child_);
    }

    HRESULT Invoke() const noexcept {
        return accessible_ ? accessible_->accDoDefaultAction(child_) : E_POINTER;
    }

private:
    ComPtr<IAccessible> accessible_;
    VARIANT child_{};
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

HRESULT ShowContextMenu(HWND target, POINT point) noexcept {
    if (!target) return E_HANDLE;
    SetLastError(ERROR_SUCCESS);
    const BOOL posted = PostMessageW(target, WM_CONTEXTMENU,
        reinterpret_cast<WPARAM>(target), MAKELPARAM(point.x, point.y));
    if (posted) return S_OK;
    const DWORD error = GetLastError();
    return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error);
}

HWND ActivePopupMenu() noexcept {
    const DWORD currentSession = []() noexcept {
        DWORD session = 0;
        return ProcessIdToSessionId(GetCurrentProcessId(), &session) ? session : (std::numeric_limits<DWORD>::max)();
    }();
    HWND previous = nullptr;
    while ((previous = FindWindowExW(nullptr, previous, L"#32768", nullptr)) != nullptr) {
        if (!IsWindowVisible(previous)) continue;
        DWORD processId = 0;
        GetWindowThreadProcessId(previous, &processId);
        DWORD session = 0;
        if (processId != 0 && ProcessIdToSessionId(processId, &session) && session == currentSession) return previous;
    }
    return nullptr;
}

HRESULT DismissPopupMenu(HWND menu) noexcept {
    if (!menu) return E_HANDLE;
    SetLastError(ERROR_SUCCESS);
    if (PostMessageW(menu, WM_CANCELMODE, 0, 0)) return S_OK;
    const DWORD error = GetLastError();
    return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error);
}
}

ShellAutomationResult NativeShellAutomation::TryHandleClick(POINT point, unsigned button) noexcept {
    ShellAutomationResult result;
    const HWND hit = WindowFromPoint(point);
    result.targetClass = WindowClass(hit);
    const HWND root = hit ? GetAncestor(hit, GA_ROOT) : nullptr;
    const std::wstring rootClass = WindowClass(root);
    const bool menu = _wcsicmp(result.targetClass.c_str(), L"#32768") == 0;
    const bool desktop = _wcsicmp(result.targetClass.c_str(), L"SysListView32") == 0;
    const bool taskbar = _wcsicmp(rootClass.c_str(), L"Shell_TrayWnd") == 0 ||
        _wcsicmp(result.targetClass.c_str(), L"MSTaskListWClass") == 0;
    const HWND popupMenu = ActivePopupMenu();
    if (popupMenu && !menu && button == 0) {
        const HRESULT dismissed = DismissPopupMenu(popupMenu);
        result.status = SUCCEEDED(dismissed) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
        result.error = SUCCEEDED(dismissed) ? ERROR_SUCCESS : ErrorCode(dismissed);
        result.stage = SUCCEEDED(dismissed) ? "native-popup-menu-dismiss-posted" :
            "native-popup-menu-dismiss-failed";
        result.targetClass = L"#32768";
        return result;
    }
    if ((!desktop && !taskbar && !menu) || (button != 0 && button != 2)) return result;

    ComApartment apartment;
    if (!apartment.Available()) {
        result.status = ShellAutomationStatus::Failed;
        result.error = ErrorCode(apartment.Result());
        result.stage = "shell-com-initialize-failed";
        return result;
    }

    AccessibleTarget accessible;
    HRESULT accessibleOperation = accessible.FromPoint(point);
    LONG accessibleRole = 0;
    const bool accessibleReady = SUCCEEDED(accessibleOperation) && accessible.Available() &&
        SUCCEEDED(accessible.Role(accessibleRole));
    if (accessibleReady) result.targetName = accessible.Name();

    if (menu) {
        if (button == 0 && accessibleReady && accessibleRole == ROLE_SYSTEM_MENUITEM) {
            accessibleOperation = accessible.Invoke();
            result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
            result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
            result.stage = SUCCEEDED(accessibleOperation) ? "native-popup-menu-msaa-invoke-ok" :
                "native-popup-menu-msaa-invoke-failed";
            return result;
        }
        accessibleOperation = DismissPopupMenu(hit);
        result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
        result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
        result.stage = SUCCEEDED(accessibleOperation) ? "native-popup-menu-dismiss-posted" :
            "native-popup-menu-dismiss-failed";
        return result;
    }

    if (desktop) {
        // SysListView32 itself represents empty desktop space. Only a list item
        // is a desktop icon and may be selected or invoked semantically.
        if (!accessibleReady || accessibleRole != ROLE_SYSTEM_LISTITEM) {
            if (button == 2) {
                accessibleOperation = ShowContextMenu(hit, point);
                result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
                result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
                result.stage = SUCCEEDED(accessibleOperation) ? "native-desktop-background-context-menu-ok" :
                    "native-desktop-background-context-menu-failed";
                return result;
            }
            result.status = ShellAutomationStatus::NotShell;
            result.stage = "desktop-empty-space";
            return result;
        }

        accessibleOperation = accessible.Select();
        if (FAILED(accessibleOperation)) {
            result.status = ShellAutomationStatus::Failed;
            result.error = ErrorCode(accessibleOperation);
            result.stage = "native-desktop-msaa-selection-failed";
            return result;
        }

        if (button == 2) {
            accessibleOperation = ShowContextMenu(hit, point);
            result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
            result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
            result.stage = SUCCEEDED(accessibleOperation) ? "native-desktop-context-menu-ok" :
                "native-desktop-context-menu-failed";
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
            accessibleOperation = accessible.Invoke();
            result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
            result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
            result.stage = SUCCEEDED(accessibleOperation) ? "native-desktop-msaa-invoke-ok" :
                "native-desktop-msaa-invoke-failed";
            lastDesktopClickTick_ = 0;
            lastDesktopWindow_ = nullptr;
            return result;
        }

        result.status = ShellAutomationStatus::Handled;
        result.stage = "native-desktop-msaa-selection-ok";
        return result;
    }

    if (button == 2) {
        accessibleOperation = ShowContextMenu(hit, point);
        result.status = SUCCEEDED(accessibleOperation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
        result.error = SUCCEEDED(accessibleOperation) ? ERROR_SUCCESS : ErrorCode(accessibleOperation);
        result.stage = SUCCEEDED(accessibleOperation) ? "native-taskbar-context-menu-ok" :
            "native-taskbar-context-menu-failed";
        return result;
    }

    if (accessibleReady) {
        accessibleOperation = accessible.Invoke();
        if (SUCCEEDED(accessibleOperation)) {
            result.status = ShellAutomationStatus::Handled;
            result.stage = "native-taskbar-msaa-invoke-ok";
            return result;
        }
    }

    // Modern taskbar providers may expose only UI Automation patterns. Keep
    // that path as a fallback after the Server-compatible MSAA route.
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

    operation = InvokeElement(automation.Get(), element.Get(), true, result.targetName);
    result.status = SUCCEEDED(operation) ? ShellAutomationStatus::Handled : ShellAutomationStatus::Failed;
    result.error = SUCCEEDED(operation) ? ERROR_SUCCESS : ErrorCode(operation);
    result.stage = SUCCEEDED(operation) ? "native-taskbar-uia-invoke-ok" : "native-taskbar-pattern-unavailable";
    return result;
}
