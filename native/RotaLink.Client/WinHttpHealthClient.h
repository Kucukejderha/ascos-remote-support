#pragma once

#include <string>

struct HealthResult final {
    bool reachable{};
    unsigned long statusCode{};
    unsigned long errorCode{};
    std::wstring message;
};

class WinHttpHealthClient final {
public:
    static HealthResult Probe();
};
