#include "SignalingClient.h"
#include "JsonLite.h"
#include <windows.h>
#include <array>
#include <stdexcept>
#include <utility>

namespace {
constexpr wchar_t HostName[] = L"45.87.173.201.nip.io";

[[noreturn]] void ThrowWinHttp(const char* operation, DWORD error = GetLastError()) {
    throw std::runtime_error(std::string(operation) + " failed, Win32=" + std::to_string(error));
}

void RequireSuccessStatus(HINTERNET request) {
    DWORD status = 0;
    DWORD bytes = sizeof(status);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX, &status, &bytes, WINHTTP_NO_HEADER_INDEX)) ThrowWinHttp("WinHttpQueryHeaders");
    if (status < 200 || status >= 300) throw std::runtime_error("RotaLink server returned HTTP " + std::to_string(status));
}

std::wstring AuthorizationHeader(std::string_view token) {
    return L"Authorization: Bearer " + JsonLite::Wide(token) + L"\r\n";
}
}

NativeWebSocket::~NativeWebSocket() { Shutdown(); }

NativeWebSocket::NativeWebSocket(NativeWebSocket&& other) noexcept : handle_(other.handle_.exchange(nullptr)) {}

NativeWebSocket& NativeWebSocket::operator=(NativeWebSocket&& other) noexcept {
    if (this != &other) {
        Shutdown();
        handle_.store(other.handle_.exchange(nullptr));
    }
    return *this;
}

bool NativeWebSocket::IsOpen() const noexcept { return handle_.load() != nullptr; }

bool NativeWebSocket::Receive(std::vector<std::uint8_t>& message, bool& binary, std::atomic_bool& stopping) {
    message.clear();
    binary = false;
    const HINTERNET socket = handle_.load();
    if (!socket) return false;
    std::array<std::uint8_t, 64 * 1024> buffer{};
    for (;;) {
        DWORD bytes = 0;
        WINHTTP_WEB_SOCKET_BUFFER_TYPE type{};
        const DWORD error = WinHttpWebSocketReceive(socket, buffer.data(), static_cast<DWORD>(buffer.size()), &bytes, &type);
        if (error != ERROR_SUCCESS) {
            if (stopping.load() || error == ERROR_WINHTTP_OPERATION_CANCELLED || error == ERROR_INVALID_HANDLE) return false;
            ThrowWinHttp("WinHttpWebSocketReceive", error);
        }
        if (type == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE) return false;
        message.insert(message.end(), buffer.begin(), buffer.begin() + bytes);
        if (message.size() > 64 * 1024) throw std::runtime_error("Control WebSocket message exceeds 64 KiB");
        if (type == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE || type == WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE) {
            binary = type == WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE;
            return true;
        }
        if (type != WINHTTP_WEB_SOCKET_UTF8_FRAGMENT_BUFFER_TYPE && type != WINHTTP_WEB_SOCKET_BINARY_FRAGMENT_BUFFER_TYPE)
            throw std::runtime_error("Unexpected WebSocket buffer type");
        binary = type == WINHTTP_WEB_SOCKET_BINARY_FRAGMENT_BUFFER_TYPE;
    }
}

void NativeWebSocket::SendText(std::string_view message) {
    std::scoped_lock lock(sendMutex_);
    const HINTERNET socket = handle_.load();
    if (!socket) throw std::runtime_error("WebSocket is closed");
    const DWORD error = WinHttpWebSocketSend(socket, WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE,
        const_cast<char*>(message.data()), static_cast<DWORD>(message.size()));
    if (error != ERROR_SUCCESS) ThrowWinHttp("WinHttpWebSocketSend(text)", error);
}

void NativeWebSocket::SendBinary(std::span<const std::uint8_t> message) {
    std::scoped_lock lock(sendMutex_);
    const HINTERNET socket = handle_.load();
    if (!socket) throw std::runtime_error("WebSocket is closed");
    const DWORD error = WinHttpWebSocketSend(socket, WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE,
        const_cast<std::uint8_t*>(message.data()), static_cast<DWORD>(message.size()));
    if (error != ERROR_SUCCESS) ThrowWinHttp("WinHttpWebSocketSend(binary)", error);
}

void NativeWebSocket::Shutdown() noexcept {
    std::scoped_lock lock(sendMutex_);
    const HINTERNET socket = handle_.exchange(nullptr);
    if (!socket) return;
    WinHttpWebSocketShutdown(socket, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
    // Closing the WinHTTP handle also cancels a synchronous receive waiting on
    // the other worker thread, so application shutdown cannot hang indefinitely.
    WinHttpCloseHandle(socket);
}

SignalingClient::SignalingClient() {
    session_ = WinHttpOpen(L"RotaLink-Native/1.2", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
        WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!session_) ThrowWinHttp("WinHttpOpen");
    WinHttpSetTimeouts(session_, 10'000, 10'000, 15'000, 15'000);
    DWORD protocols = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
    WinHttpSetOption(session_, WINHTTP_OPTION_SECURE_PROTOCOLS, &protocols, sizeof(protocols));
    connection_ = WinHttpConnect(session_, HostName, INTERNET_DEFAULT_HTTPS_PORT, 0);
    if (!connection_) {
        const DWORD error = GetLastError(); WinHttpCloseHandle(session_); session_ = nullptr;
        ThrowWinHttp("WinHttpConnect", error);
    }
}

SignalingClient::~SignalingClient() {
    if (connection_) WinHttpCloseHandle(connection_);
    if (session_) WinHttpCloseHandle(session_);
}

std::string SignalingClient::Post(std::wstring_view path, std::string_view body, std::string_view bearerToken) {
    HINTERNET request = WinHttpOpenRequest(connection_, L"POST", std::wstring(path).c_str(), nullptr,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE);
    if (!request) ThrowWinHttp("WinHttpOpenRequest(POST)");
    struct RequestCloser { HINTERNET value; ~RequestCloser() { if (value) WinHttpCloseHandle(value); } } closer{request};
    std::wstring headers = L"Content-Type: application/json; charset=utf-8\r\nAccept: application/json\r\n";
    if (!bearerToken.empty()) headers += AuthorizationHeader(bearerToken);
    if (!WinHttpSendRequest(request, headers.c_str(), static_cast<DWORD>(headers.size()),
        body.empty() ? WINHTTP_NO_REQUEST_DATA : const_cast<char*>(body.data()), static_cast<DWORD>(body.size()),
        static_cast<DWORD>(body.size()), 0) || !WinHttpReceiveResponse(request, nullptr)) ThrowWinHttp("WinHttp POST");
    RequireSuccessStatus(request);
    std::string response;
    for (;;) {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request, &available)) ThrowWinHttp("WinHttpQueryDataAvailable");
        if (available == 0) break;
        if (response.size() + available > 1024 * 1024) throw std::runtime_error("Server JSON response exceeds 1 MiB");
        const std::size_t offset = response.size();
        response.resize(offset + available);
        DWORD read = 0;
        if (!WinHttpReadData(request, response.data() + offset, available, &read)) ThrowWinHttp("WinHttpReadData");
        response.resize(offset + read);
    }
    return response;
}

NativeHostSession SignalingClient::CreateSession() {
    wchar_t computerName[MAX_COMPUTERNAME_LENGTH + 1]{};
    DWORD computerNameLength = ARRAYSIZE(computerName);
    if (!GetComputerNameW(computerName, &computerNameLength)) wcscpy_s(computerName, L"Windows PC");
    const std::string publicKey = identity_.PublicKeySpkiBase64();
    const std::string registrationBody = "{\"publicKeySpkiBase64\":\"" + JsonLite::Escape(publicKey) +
        "\",\"displayName\":\"" + JsonLite::Escape(JsonLite::Utf8(computerName)) + "\"}";
    const std::string registration = Post(L"/v1/devices", registrationBody);
    NativeHostSession result;
    result.deviceId = JsonLite::StringValue(registration, "deviceId");
    const std::wstring challengePath = L"/v1/devices/" + JsonLite::Wide(result.deviceId) + L"/challenge";
    const std::string challenge = Post(challengePath, "{}");
    const std::string challengeId = JsonLite::StringValue(challenge, "challengeId");
    const auto nonce = CngDeviceIdentity::DecodeBase64(JsonLite::StringValue(challenge, "nonceBase64"));
    const std::string signature = identity_.SignBase64(nonce);
    const std::string verifyBody = "{\"challengeId\":\"" + JsonLite::Escape(challengeId) +
        "\",\"signatureBase64\":\"" + JsonLite::Escape(signature) + "\"}";
    const std::wstring verifyPath = L"/v1/devices/" + JsonLite::Wide(result.deviceId) + L"/verify";
    const std::string access = Post(verifyPath, verifyBody);
    result.accessToken = JsonLite::StringValue(access, "accessToken");
    const std::string code = Post(L"/v1/support-codes", "{}", result.accessToken);
    result.sessionId = JsonLite::StringValue(code, "sessionId");
    result.code = JsonLite::StringValue(code, "code");
    if (result.code.size() != 9) throw std::runtime_error("Server returned an invalid support code");
    return result;
}

NativeWebSocket SignalingClient::ConnectHostSocket(const NativeHostSession& session, std::string_view channel) {
    if (channel != "control" && channel != "video") throw std::invalid_argument("Invalid host WebSocket channel");
    const std::wstring path = L"/v1/sessions/" + JsonLite::Wide(session.sessionId) +
        L"/signal?role=host&channel=" + JsonLite::Wide(channel);
    HINTERNET request = WinHttpOpenRequest(connection_, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE);
    if (!request) ThrowWinHttp("WinHttpOpenRequest(WebSocket)");
    struct RequestCloser { HINTERNET value; ~RequestCloser() { if (value) WinHttpCloseHandle(value); } } closer{request};
    if (!WinHttpSetOption(request, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, nullptr, 0)) ThrowWinHttp("WebSocket upgrade option");
    const std::wstring authorization = AuthorizationHeader(session.accessToken);
    if (!WinHttpSendRequest(request, authorization.c_str(), static_cast<DWORD>(authorization.size()),
        WINHTTP_NO_REQUEST_DATA, 0, 0, 0) || !WinHttpReceiveResponse(request, nullptr)) ThrowWinHttp("WebSocket handshake");
    DWORD status = 0, statusBytes = sizeof(status);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX, &status, &statusBytes, WINHTTP_NO_HEADER_INDEX)) ThrowWinHttp("WebSocket status");
    if (status != 101) throw std::runtime_error("WebSocket upgrade returned HTTP " + std::to_string(status));
    HINTERNET socket = WinHttpWebSocketCompleteUpgrade(request, 0);
    if (!socket) ThrowWinHttp("WinHttpWebSocketCompleteUpgrade");
    return NativeWebSocket(socket);
}
