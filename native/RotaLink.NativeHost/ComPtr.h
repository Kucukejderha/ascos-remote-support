#pragma once
#include <cstddef>
#include <utility>
#include <windows.h>

// Minimal WRL-compatible COM smart pointer. Keeps the native capture engine
// independent of the Windows SDK WRL headers (wrl/client.h), which are not
// shipped with every SDK installer flavor.
template <typename T>
class ComPtr final {
public:
    ComPtr() noexcept = default;
    ComPtr(std::nullptr_t) noexcept {}
    ComPtr(T* ptr) noexcept : ptr_(ptr) { if (ptr_) ptr_->AddRef(); }
    ~ComPtr() { Reset(); }
    ComPtr(const ComPtr& other) noexcept : ptr_(other.ptr_) { if (ptr_) ptr_->AddRef(); }
    ComPtr& operator=(const ComPtr& other) noexcept {
        if (this != &other) { ComPtr temp(other); Swap(temp); }
        return *this;
    }
    ComPtr(ComPtr&& other) noexcept : ptr_(other.ptr_) { other.ptr_ = nullptr; }
    ComPtr& operator=(ComPtr&& other) noexcept {
        if (this != &other) { Reset(); ptr_ = other.ptr_; other.ptr_ = nullptr; }
        return *this;
    }

    T* Get() const noexcept { return ptr_; }
    T** GetAddressOf() noexcept { return &ptr_; }
    T** operator&() noexcept { return &ptr_; }
    T* Detach() noexcept { T* result = ptr_; ptr_ = nullptr; return result; }
    void Reset() noexcept { if (ptr_) { ptr_->Release(); ptr_ = nullptr; } }
    void Swap(ComPtr& other) noexcept { std::swap(ptr_, other.ptr_); }

    T* operator->() const noexcept { return ptr_; }
    T& operator*() const noexcept { return *ptr_; }
    explicit operator bool() const noexcept { return ptr_ != nullptr; }

    template <typename U>
    HRESULT As(U** out) const noexcept {
        if (!out) return E_POINTER;
        if (*out) { (*out)->Release(); *out = nullptr; }
        return ptr_ ? ptr_->QueryInterface(__uuidof(U), reinterpret_cast<void**>(out)) : E_POINTER;
    }

private:
    T* ptr_ = nullptr;
};
