#include "WinHttpHealthClient.h"
#include "NativeHandles.h"
#include <windows.h>
#include <winhttp.h>

namespace {
void CloseInternet(HINTERNET handle) { if (handle) WinHttpCloseHandle(handle); }
class UniqueInternet final {
public:
    explicit UniqueInternet(HINTERNET value = nullptr) noexcept : value_(value) {}
    ~UniqueInternet() { CloseInternet(value_); }
    UniqueInternet(const UniqueInternet&) = delete;
    UniqueInternet& operator=(const UniqueInternet&) = delete;
    [[nodiscard]] HINTERNET get() const noexcept { return value_; }
    [[nodiscard]] explicit operator bool() const noexcept { return value_ != nullptr; }
private:
    HINTERNET value_{};
};
}

HealthResult WinHttpHealthClient::Probe() {
    HealthResult result;
    UniqueInternet session(WinHttpOpen(L"RotaLink-Native/1.2", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
        WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0));
    if (!session) { result.errorCode = GetLastError(); result.message = L"WinHTTP başlatılamadı."; return result; }
    WinHttpSetTimeouts(session.get(), 5000, 5000, 5000, 5000);
    DWORD protocols = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
    WinHttpSetOption(session.get(), WINHTTP_OPTION_SECURE_PROTOCOLS,
        &protocols, sizeof(protocols));
    UniqueInternet connection(WinHttpConnect(session.get(), L"45.87.173.201.nip.io",
        INTERNET_DEFAULT_HTTPS_PORT, 0));
    if (!connection) { result.errorCode = GetLastError(); result.message = L"Sunucu bağlantısı açılamadı."; return result; }
    UniqueInternet request(WinHttpOpenRequest(connection.get(), L"GET", L"/health", nullptr,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE));
    if (!request) { result.errorCode = GetLastError(); result.message = L"Sağlık isteği oluşturulamadı."; return result; }
    if (!WinHttpSendRequest(request.get(), WINHTTP_NO_ADDITIONAL_HEADERS, 0,
        WINHTTP_NO_REQUEST_DATA, 0, 0, 0) || !WinHttpReceiveResponse(request.get(), nullptr)) {
        result.errorCode = GetLastError();
        result.message = L"RotaLink sunucusuna güvenli bağlantı kurulamadı.";
        return result;
    }
    DWORD status = 0;
    DWORD size = sizeof(status);
    if (!WinHttpQueryHeaders(request.get(), WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX, &status, &size, WINHTTP_NO_HEADER_INDEX)) {
        result.errorCode = GetLastError(); result.message = L"Sunucu yanıtı okunamadı."; return result;
    }
    result.statusCode = status;
    result.reachable = status >= 200 && status < 300;
    result.message = result.reachable ? L"RotaLink sunucusuna güvenli bağlantı hazır."
                                      : L"RotaLink sunucusu beklenmeyen bir yanıt verdi.";
    return result;
}
