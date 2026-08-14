#include "InputPipe.h"
#include "Diagnostics.h"
#include "JsonLite.h"
#include "NativeInputEngine.h"
#include <algorithm>
#include <array>
#include <limits>
#include <vector>

namespace {
constexpr std::uint32_t MaximumMessageBytes = 64 * 1024;

std::wstring PipeName(DWORD processId) {
    return L"\\\\.\\pipe\\RotaLink.Native." + std::to_wstring(processId) + L".Input.v1";
}

struct ClientValidation final {
    bool accepted{};
    DWORD error{};
    ULONG pipeSession{};
    DWORD helperSession{};
    const wchar_t* stage{L"unknown"};
};

ClientValidation ValidateClient(HANDLE pipe, ULONG processId, DWORD allowedProcessId) noexcept {
    ClientValidation result{};
    if (processId != allowedProcessId) {
        result.stage = L"pid-mismatch";
        return result;
    }

    if (!GetNamedPipeClientSessionId(pipe, &result.pipeSession)) {
        result.error = GetLastError();
        result.stage = L"pipe-session-query-failed";
        return result;
    }
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &result.helperSession)) {
        result.error = GetLastError();
        result.stage = L"helper-session-query-failed";
        return result;
    }
    if (result.pipeSession != result.helperSession) {
        result.stage = L"session-mismatch";
        return result;
    }

    // GetNamedPipeClientProcessId/GetNamedPipeClientSessionId are kernel supplied values.
    // Opening the UI process again is both redundant and incorrect: an elevated helper can
    // receive ERROR_ACCESS_DENIED for a valid interactive client on hardened Server systems.
    result.accepted = true;
    result.stage = L"kernel-identity-ok";
    return result;
}

bool WriteAll(HANDLE handle, const void* buffer, DWORD length) noexcept {
    auto* current = static_cast<const std::uint8_t*>(buffer);
    while (length > 0) {
        DWORD written = 0;
        if (!WriteFile(handle, current, length, &written, nullptr) || written == 0) return false;
        current += written;
        length -= written;
    }
    return true;
}

bool ReadAll(HANDLE handle, void* buffer, DWORD length) noexcept {
    auto* current = static_cast<std::uint8_t*>(buffer);
    while (length > 0) {
        DWORD read = 0;
        if (!ReadFile(handle, current, length, &read, nullptr) || read == 0) return false;
        current += read;
        length -= read;
    }
    return true;
}

bool WriteMessage(HANDLE handle, std::string_view message) noexcept {
    if (message.empty() || message.size() > MaximumMessageBytes) return false;
    const auto length = static_cast<std::uint32_t>(message.size());
    return WriteAll(handle, &length, sizeof(length)) && WriteAll(handle, message.data(), length);
}

bool ReadMessage(HANDLE handle, std::string& message) noexcept {
    std::uint32_t length = 0;
    if (!ReadAll(handle, &length, sizeof(length)) || length == 0 || length > MaximumMessageBytes) return false;
    message.resize(length);
    return ReadAll(handle, message.data(), length);
}

std::string ResultJson(const NativeInputResult& result) {
    return "{\"type\":\"control-result\",\"ok\":" + std::string(result.accepted ? "true" : "false") +
        ",\"stage\":\"" + JsonLite::Escape(result.stage) + "\",\"error\":" + std::to_string(result.error) +
        ",\"desktop\":\"" + JsonLite::Escape(result.desktop) + "\",\"eventType\":\"" +
        JsonLite::Escape(result.eventType) + "\"}";
}
}

InputPipeClient::~InputPipeClient() { Close(); }

bool InputPipeClient::Connect() noexcept {
    if (pipe_ != INVALID_HANDLE_VALUE) return true;
    const std::wstring name = PipeName(clientProcessId_);
    for (int attempt = 0; attempt < 20; ++attempt) {
        pipe_ = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, nullptr);
        if (pipe_ != INVALID_HANDLE_VALUE) {
            DWORD mode = PIPE_READMODE_BYTE;
            if (!SetNamedPipeHandleState(pipe_, &mode, nullptr, nullptr)) { Close(); return false; }
            return true;
        }
        const DWORD error = GetLastError();
        if (error != ERROR_PIPE_BUSY && error != ERROR_FILE_NOT_FOUND) return false;
        WaitNamedPipeW(name.c_str(), 100);
    }
    return false;
}

void InputPipeClient::Close() noexcept {
    if (pipe_ != INVALID_HANDLE_VALUE) CloseHandle(pipe_);
    pipe_ = INVALID_HANDLE_VALUE;
}

bool InputPipeClient::TryDispatch(std::string_view requestJson, std::string& responseJson) noexcept {
    for (int attempt = 0; attempt < 2; ++attempt) {
        if (!Connect()) continue;
        if (WriteMessage(pipe_, requestJson) && ReadMessage(pipe_, responseJson)) return true;
        Close();
    }
    return false;
}

int InputPipeServer::Run() {
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, FALSE};
    const std::wstring name = PipeName(allowedClientProcessId_);
    const std::wstring stopName = L"Global\\RotaLink.Native." +
        std::to_wstring(allowedClientProcessId_) + L".HelperStop";
    HANDLE stop = CreateEventW(&security, TRUE, FALSE, stopName.c_str());
    if (!stop) return 31;
    struct HandleCloser { HANDLE value; ~HandleCloser() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); } } stopCloser{stop};
    Diagnostics::Write(L"Native interactive input helper is ready. Pipe=" + name + L".");
    for (;;) {
        if (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0) return 0;
        HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, MaximumMessageBytes + 4, MaximumMessageBytes + 4, 0, &security);
        if (pipe == INVALID_HANDLE_VALUE) return 32;
        const BOOL connected = ConnectNamedPipe(pipe, nullptr) ? TRUE : GetLastError() == ERROR_PIPE_CONNECTED;
        if (!connected) { CloseHandle(pipe); continue; }
        if (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0) {
            DisconnectNamedPipe(pipe); CloseHandle(pipe); return 0;
        }
        ULONG clientProcessId = 0;
        if (!GetNamedPipeClientProcessId(pipe, &clientProcessId)) {
            Diagnostics::Write(L"Rejected input pipe client: kernel PID query failed. Win32=" +
                std::to_wstring(GetLastError()) + L".");
            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
            continue;
        }
        const ClientValidation validation = ValidateClient(pipe, clientProcessId, allowedClientProcessId_);
        if (!validation.accepted) {
            Diagnostics::Write(L"Rejected input pipe client. Stage=" + std::wstring(validation.stage) +
                L", ExpectedPid=" + std::to_wstring(allowedClientProcessId_) + L", ActualPid=" +
                std::to_wstring(clientProcessId) + L", PipeSession=" + std::to_wstring(validation.pipeSession) +
                L", HelperSession=" + std::to_wstring(validation.helperSession) + L", Win32=" +
                std::to_wstring(validation.error) + L".");
            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
            continue;
        }
        Diagnostics::Write(L"Native input IPC client authenticated by kernel identity. Pid=" +
            std::to_wstring(clientProcessId) + L", Session=" + std::to_wstring(validation.pipeSession) + L".");
        {
            NativeInputEngine input;
            std::string request;
            while (ReadMessage(pipe, request)) {
                if (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0) break;
                const NativeInputResult result = input.Dispatch(request);
                if (!result.accepted || result.eventType != "move") {
                    Diagnostics::Write(L"Native input result. Accepted=" +
                        std::wstring(result.accepted ? L"true" : L"false") + L", Stage=" +
                        std::wstring(result.stage.begin(), result.stage.end()) + L", Win32=" +
                        std::to_wstring(result.error) + L", Desktop=" +
                        std::wstring(result.desktop.begin(), result.desktop.end()) + L", Event=" +
                        std::wstring(result.eventType.begin(), result.eventType.end()) + L", NormalizedX=" +
                        std::to_wstring(result.normalizedX) + L", NormalizedY=" +
                        std::to_wstring(result.normalizedY) + L", Button=" +
                        std::to_wstring(result.button) + L".");
                }
                if (!WriteMessage(pipe, ResultJson(result))) break;
            }
        }
        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}
