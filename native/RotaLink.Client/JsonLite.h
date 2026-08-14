#pragma once

#include <string>
#include <string_view>

namespace JsonLite {
[[nodiscard]] std::string Escape(std::string_view value);
[[nodiscard]] std::string StringValue(std::string_view document, std::string_view property);
[[nodiscard]] double NumberValue(std::string_view document, std::string_view property, double fallback);
[[nodiscard]] bool BooleanValue(std::string_view document, std::string_view property, bool fallback);
[[nodiscard]] std::string Utf8(std::wstring_view value);
[[nodiscard]] std::wstring Wide(std::string_view value);
}
