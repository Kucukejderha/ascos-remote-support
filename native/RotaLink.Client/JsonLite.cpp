#include "JsonLite.h"
#include <windows.h>
#include <iomanip>
#include <cstdlib>
#include <sstream>
#include <stdexcept>

namespace {
unsigned HexValue(char value) {
    if (value >= '0' && value <= '9') return static_cast<unsigned>(value - '0');
    if (value >= 'a' && value <= 'f') return static_cast<unsigned>(value - 'a' + 10);
    if (value >= 'A' && value <= 'F') return static_cast<unsigned>(value - 'A' + 10);
    throw std::runtime_error("Invalid JSON Unicode escape");
}

void AppendUtf8(std::string& target, unsigned codePoint) {
    if (codePoint <= 0x7F) target.push_back(static_cast<char>(codePoint));
    else if (codePoint <= 0x7FF) {
        target.push_back(static_cast<char>(0xC0 | (codePoint >> 6)));
        target.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    } else {
        target.push_back(static_cast<char>(0xE0 | (codePoint >> 12)));
        target.push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F)));
        target.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    }
}
}

std::string JsonLite::Escape(std::string_view value) {
    std::ostringstream escaped;
    for (const unsigned char item : value) {
        switch (item) {
        case '"': escaped << "\\\""; break;
        case '\\': escaped << "\\\\"; break;
        case '\b': escaped << "\\b"; break;
        case '\f': escaped << "\\f"; break;
        case '\n': escaped << "\\n"; break;
        case '\r': escaped << "\\r"; break;
        case '\t': escaped << "\\t"; break;
        default:
            if (item < 0x20) escaped << "\\u" << std::hex << std::setw(4) << std::setfill('0')
                                     << static_cast<unsigned>(item) << std::dec;
            else escaped << static_cast<char>(item);
        }
    }
    return escaped.str();
}

std::string JsonLite::StringValue(std::string_view document, std::string_view property) {
    const std::string marker = "\"" + std::string(property) + "\"";
    std::size_t position = document.find(marker);
    if (position == std::string_view::npos) throw std::runtime_error("JSON property is missing: " + std::string(property));
    position = document.find(':', position + marker.size());
    if (position == std::string_view::npos) throw std::runtime_error("JSON property separator is missing");
    position = document.find_first_not_of(" \t\r\n", position + 1);
    if (position == std::string_view::npos || document[position] != '"')
        throw std::runtime_error("JSON property is not a string: " + std::string(property));
    ++position;
    std::string result;
    while (position < document.size()) {
        char value = document[position++];
        if (value == '"') return result;
        if (value != '\\') { result.push_back(value); continue; }
        if (position >= document.size()) break;
        const char escaped = document[position++];
        switch (escaped) {
        case '"': result.push_back('"'); break;
        case '\\': result.push_back('\\'); break;
        case '/': result.push_back('/'); break;
        case 'b': result.push_back('\b'); break;
        case 'f': result.push_back('\f'); break;
        case 'n': result.push_back('\n'); break;
        case 'r': result.push_back('\r'); break;
        case 't': result.push_back('\t'); break;
        case 'u': {
            if (position + 4 > document.size()) throw std::runtime_error("Incomplete JSON Unicode escape");
            unsigned codePoint = 0;
            for (unsigned index = 0; index < 4; ++index) codePoint = (codePoint << 4) | HexValue(document[position++]);
            AppendUtf8(result, codePoint);
            break;
        }
        default: throw std::runtime_error("Unsupported JSON escape");
        }
    }
    throw std::runtime_error("Unterminated JSON string");
}

double JsonLite::NumberValue(std::string_view document, std::string_view property, double fallback) {
    const std::string marker = "\"" + std::string(property) + "\"";
    std::size_t position = document.find(marker);
    if (position == std::string_view::npos) return fallback;
    position = document.find(':', position + marker.size());
    if (position == std::string_view::npos) return fallback;
    position = document.find_first_not_of(" \t\r\n", position + 1);
    if (position == std::string_view::npos) return fallback;
    const std::string tail(document.substr(position));
    char* end = nullptr;
    const double value = std::strtod(tail.c_str(), &end);
    return end != tail.c_str() ? value : fallback;
}

bool JsonLite::BooleanValue(std::string_view document, std::string_view property, bool fallback) {
    const std::string marker = "\"" + std::string(property) + "\"";
    std::size_t position = document.find(marker);
    if (position == std::string_view::npos) return fallback;
    position = document.find(':', position + marker.size());
    if (position == std::string_view::npos) return fallback;
    position = document.find_first_not_of(" \t\r\n", position + 1);
    if (position == std::string_view::npos) return fallback;
    if (document.substr(position, 4) == "true") return true;
    if (document.substr(position, 5) == "false") return false;
    return fallback;
}

std::string JsonLite::Utf8(std::wstring_view value) {
    if (value.empty()) return {};
    const int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) throw std::runtime_error("WideCharToMultiByte failed");
    std::string result(static_cast<std::size_t>(bytes), '\0');
    if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
        result.data(), bytes, nullptr, nullptr)) throw std::runtime_error("WideCharToMultiByte failed");
    return result;
}

std::wstring JsonLite::Wide(std::string_view value) {
    if (value.empty()) return {};
    const int characters = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), nullptr, 0);
    if (characters <= 0) throw std::runtime_error("MultiByteToWideChar failed");
    std::wstring result(static_cast<std::size_t>(characters), L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
        result.data(), characters)) throw std::runtime_error("MultiByteToWideChar failed");
    return result;
}
