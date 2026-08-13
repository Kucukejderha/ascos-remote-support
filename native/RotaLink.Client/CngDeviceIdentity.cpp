#include "CngDeviceIdentity.h"
#include <wincrypt.h>
#include <array>
#include <stdexcept>

namespace {
void Check(NTSTATUS status, const char* operation) {
    if (!BCRYPT_SUCCESS(status)) throw std::runtime_error(std::string(operation) + " failed, NTSTATUS=" +
        std::to_string(static_cast<unsigned long>(status)));
}

std::vector<std::uint8_t> Sha256(std::span<const std::uint8_t> input) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0), "BCryptOpenAlgorithmProvider(SHA256)");
    struct AlgorithmCloser { BCRYPT_ALG_HANDLE value; ~AlgorithmCloser() { if (value) BCryptCloseAlgorithmProvider(value, 0); } } closer{algorithm};
    DWORD objectBytes = 0, resultBytes = 0;
    Check(BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectBytes),
        sizeof(objectBytes), &resultBytes, 0), "BCryptGetProperty");
    std::vector<std::uint8_t> object(objectBytes), hash(32);
    BCRYPT_HASH_HANDLE handle = nullptr;
    Check(BCryptCreateHash(algorithm, &handle, object.data(), static_cast<ULONG>(object.size()), nullptr, 0, 0), "BCryptCreateHash");
    struct HashCloser { BCRYPT_HASH_HANDLE value; ~HashCloser() { if (value) BCryptDestroyHash(value); } } hashCloser{handle};
    Check(BCryptHashData(handle, const_cast<PUCHAR>(input.data()), static_cast<ULONG>(input.size()), 0), "BCryptHashData");
    Check(BCryptFinishHash(handle, hash.data(), static_cast<ULONG>(hash.size()), 0), "BCryptFinishHash");
    return hash;
}

std::string EncodeBase64(std::span<const std::uint8_t> value) {
    DWORD characters = 0;
    if (!CryptBinaryToStringA(value.data(), static_cast<DWORD>(value.size()),
        CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &characters)) throw std::runtime_error("Base64 sizing failed");
    std::string encoded(characters, '\0');
    if (!CryptBinaryToStringA(value.data(), static_cast<DWORD>(value.size()),
        CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, encoded.data(), &characters)) throw std::runtime_error("Base64 encoding failed");
    while (!encoded.empty() && encoded.back() == '\0') encoded.pop_back();
    return encoded;
}
}

CngDeviceIdentity::CngDeviceIdentity() {
    Check(BCryptOpenAlgorithmProvider(&algorithm_, BCRYPT_ECDSA_P256_ALGORITHM, nullptr, 0), "BCryptOpenAlgorithmProvider(P256)");
    try {
        Check(BCryptGenerateKeyPair(algorithm_, &key_, 256, 0), "BCryptGenerateKeyPair");
        Check(BCryptFinalizeKeyPair(key_, 0), "BCryptFinalizeKeyPair");
    } catch (...) {
        if (key_) BCryptDestroyKey(key_);
        BCryptCloseAlgorithmProvider(algorithm_, 0);
        algorithm_ = nullptr; key_ = nullptr;
        throw;
    }
}

CngDeviceIdentity::~CngDeviceIdentity() {
    if (key_) BCryptDestroyKey(key_);
    if (algorithm_) BCryptCloseAlgorithmProvider(algorithm_, 0);
}

std::string CngDeviceIdentity::PublicKeySpkiBase64() const {
    ULONG bytes = 0;
    Check(BCryptExportKey(key_, nullptr, BCRYPT_ECCPUBLIC_BLOB, nullptr, 0, &bytes, 0), "BCryptExportKey(size)");
    std::vector<std::uint8_t> blob(bytes);
    Check(BCryptExportKey(key_, nullptr, BCRYPT_ECCPUBLIC_BLOB, blob.data(), bytes, &bytes, 0), "BCryptExportKey");
    if (blob.size() != sizeof(BCRYPT_ECCKEY_BLOB) + 64) throw std::runtime_error("Unexpected P-256 public key size");
    const auto* header = reinterpret_cast<const BCRYPT_ECCKEY_BLOB*>(blob.data());
    if (header->dwMagic != BCRYPT_ECDSA_PUBLIC_P256_MAGIC || header->cbKey != 32)
        throw std::runtime_error("Unexpected P-256 public key format");
    constexpr std::array<std::uint8_t, 27> prefix{0x30,0x59,0x30,0x13,0x06,0x07,0x2A,0x86,0x48,
        0xCE,0x3D,0x02,0x01,0x06,0x08,0x2A,0x86,0x48,0xCE,0x3D,0x03,0x01,0x07,0x03,0x42,0x00,0x04};
    std::vector<std::uint8_t> spki(prefix.begin(), prefix.end());
    spki.insert(spki.end(), blob.begin() + sizeof(BCRYPT_ECCKEY_BLOB), blob.end());
    return EncodeBase64(spki);
}

std::string CngDeviceIdentity::SignBase64(std::span<const std::uint8_t> message) const {
    const auto hash = Sha256(message);
    ULONG bytes = 0;
    Check(BCryptSignHash(key_, nullptr, const_cast<PUCHAR>(hash.data()), static_cast<ULONG>(hash.size()),
        nullptr, 0, &bytes, 0), "BCryptSignHash(size)");
    std::vector<std::uint8_t> signature(bytes);
    Check(BCryptSignHash(key_, nullptr, const_cast<PUCHAR>(hash.data()), static_cast<ULONG>(hash.size()),
        signature.data(), bytes, &bytes, 0), "BCryptSignHash");
    signature.resize(bytes);
    return EncodeBase64(signature);
}

std::vector<std::uint8_t> CngDeviceIdentity::DecodeBase64(const std::string& value) {
    DWORD bytes = 0;
    if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64,
        nullptr, &bytes, nullptr, nullptr)) throw std::runtime_error("Invalid Base64 value");
    std::vector<std::uint8_t> decoded(bytes);
    if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64,
        decoded.data(), &bytes, nullptr, nullptr)) throw std::runtime_error("Base64 decoding failed");
    decoded.resize(bytes);
    return decoded;
}
