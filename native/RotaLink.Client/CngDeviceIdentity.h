#pragma once

#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <string>
#include <span>
#include <vector>

class CngDeviceIdentity final {
public:
    CngDeviceIdentity();
    ~CngDeviceIdentity();
    CngDeviceIdentity(const CngDeviceIdentity&) = delete;
    CngDeviceIdentity& operator=(const CngDeviceIdentity&) = delete;
    [[nodiscard]] std::string PublicKeySpkiBase64() const;
    [[nodiscard]] std::string SignBase64(std::span<const std::uint8_t> message) const;
    [[nodiscard]] static std::vector<std::uint8_t> DecodeBase64(const std::string& value);
private:
    BCRYPT_ALG_HANDLE algorithm_{};
    BCRYPT_KEY_HANDLE key_{};
};
