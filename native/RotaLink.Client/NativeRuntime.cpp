#include "NativeRuntime.h"
#include "Diagnostics.h"
#include "InputPipe.h"
#include <userenv.h>
#include <wtsapi32.h>
#include <array>
#include <chrono>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {
constexpr wchar_t ServiceName[] = L"RotaLinkNativeRuntime";
constexpr wchar_t ServiceDisplayName[] = L"RotaLink Native Control Runtime";
constexpr DWORD ServiceAccess = SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP | SERVICE_CHANGE_CONFIG | DELETE;
SERVICE_STATUS_HANDLE statusHandle{};
SERVICE_STATUS serviceStatus{};
HANDLE stopEvent{};
HANDLE reconcileEvent{};
DWORD serviceClientProcessId{};
HANDLE helperProcess{};

HANDLE LaunchInteractiveHelper(DWORD clientProcessId);

[[noreturn]] void ThrowWin32(const char* operation, DWORD error = GetLastError()) {
    throw std::runtime_error(std::string(operation) + " failed, Win32=" + std::to_string(error));
}

std::wstring ExecutablePath() {
    std::vector<wchar_t> buffer(4096);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) ThrowWin32("GetModuleFileNameW");
    return std::wstring(buffer.data(), length);
}

bool QueryState(SC_HANDLE service, DWORD& state) noexcept {
    SERVICE_STATUS_PROCESS status{};
    DWORD bytes = 0;
    if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, reinterpret_cast<BYTE*>(&status),
        sizeof(status), &bytes)) return false;
    state = status.dwCurrentState;
    return true;
}

bool WaitForState(SC_HANDLE service, DWORD expected, DWORD milliseconds) noexcept {
    const auto deadline = GetTickCount64() + milliseconds;
    for (;;) {
        DWORD state = 0;
        if (!QueryState(service, state)) return false;
        if (state == expected) return true;
        if (GetTickCount64() >= deadline) return false;
        Sleep(100);
    }
}

void StopServiceIfNeeded(SC_HANDLE service) noexcept {
    DWORD state = 0;
    if (!QueryState(service, state) || state == SERVICE_STOPPED) return;
    SERVICE_STATUS status{};
    ControlService(service, SERVICE_CONTROL_STOP, &status);
    WaitForState(service, SERVICE_STOPPED, 10'000);
}

void ReportStatus(DWORD state, DWORD accepted, DWORD error = ERROR_SUCCESS, DWORD waitHint = 0) noexcept {
    serviceStatus.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
    serviceStatus.dwCurrentState = state;
    serviceStatus.dwControlsAccepted = accepted;
    serviceStatus.dwWin32ExitCode = error;
    serviceStatus.dwWaitHint = waitHint;
    serviceStatus.dwCheckPoint = state == SERVICE_START_PENDING || state == SERVICE_STOP_PENDING
        ? serviceStatus.dwCheckPoint + 1 : 0;
    if (statusHandle) SetServiceStatus(statusHandle, &serviceStatus);
}

DWORD WINAPI ServiceHandler(DWORD control, DWORD, void*, void*) {
    if (control == SERVICE_CONTROL_STOP && stopEvent) {
        ReportStatus(SERVICE_STOP_PENDING, 0, ERROR_SUCCESS, 10'000);
        SetEvent(stopEvent);
    }
    if (control == SERVICE_CONTROL_SESSIONCHANGE && reconcileEvent) SetEvent(reconcileEvent);
    return ERROR_SUCCESS;
}

void StopHelper() noexcept {
    HANDLE process = helperProcess;
    helperProcess = nullptr;
    if (!process) return;
    const std::wstring stopName = L"Global\\RotaLink.Native." +
        std::to_wstring(serviceClientProcessId) + L".HelperStop";
    HANDLE helperStop = OpenEventW(EVENT_MODIFY_STATE, FALSE, stopName.c_str());
    if (helperStop) {
        SetEvent(helperStop);
        CloseHandle(helperStop);
        // Wake a helper waiting synchronously in ConnectNamedPipe so it can
        // observe the stop event and release held input before exiting.
        const std::wstring pipeName = L"\\\\.\\pipe\\RotaLink.Native." +
            std::to_wstring(serviceClientProcessId) + L".Input.v1";
        HANDLE wake = CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (wake != INVALID_HANDLE_VALUE) CloseHandle(wake);
    }
    if (WaitForSingleObject(process, 0) == WAIT_TIMEOUT) {
        if (WaitForSingleObject(process, 5'000) == WAIT_TIMEOUT) {
            TerminateProcess(process, 0x524F5441);
            WaitForSingleObject(process, 5'000);
        }
    }
    CloseHandle(process);
}

DWORD WINAPI SessionNotificationThread(void*) {
    DWORD sessionId = 0;
    if (!ProcessIdToSessionId(serviceClientProcessId, &sessionId)) return GetLastError();
    for (;;) {
        if (WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) return ERROR_SUCCESS;
        WTS_CONNECTSTATE_CLASS state = WTSDisconnected;
        LPWSTR buffer = nullptr;
        DWORD bytes = 0;
        if (WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, sessionId, WTSConnectState,
            &buffer, &bytes) && bytes >= sizeof(state)) {
            state = *reinterpret_cast<WTS_CONNECTSTATE_CLASS*>(buffer);
        }
        if (buffer) WTSFreeMemory(buffer);
        if (state == WTSActive) {
            if (!helperProcess || WaitForSingleObject(helperProcess, 0) != WAIT_TIMEOUT) {
                StopHelper();
                try {
                    helperProcess = LaunchInteractiveHelper(serviceClientProcessId);
                    Diagnostics::Write(L"WTS monitor started native helper in active session " +
                        std::to_wstring(sessionId) + L".");
                } catch (const std::exception& error) {
                    const std::string message(error.what());
                    Diagnostics::Write(L"WTS helper reconciliation failed: " +
                        std::wstring(message.begin(), message.end()));
                }
            }
        } else StopHelper();
        HANDLE events[]{stopEvent, reconcileEvent};
        if (WaitForMultipleObjects(ARRAYSIZE(events), events, FALSE, 1'000) == WAIT_OBJECT_0) return ERROR_SUCCESS;
    }
}

bool EnablePrivilege(const wchar_t* name) noexcept {
    HANDLE rawToken = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, &rawToken)) return false;
    LUID luid{};
    if (!LookupPrivilegeValueW(nullptr, name, &luid)) { CloseHandle(rawToken); return false; }
    TOKEN_PRIVILEGES privileges{};
    privileges.PrivilegeCount = 1;
    privileges.Privileges[0].Luid = luid;
    privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    SetLastError(ERROR_SUCCESS);
    const BOOL changed = AdjustTokenPrivileges(rawToken, FALSE, &privileges, 0, nullptr, nullptr);
    const DWORD error = GetLastError();
    CloseHandle(rawToken);
    return changed && error == ERROR_SUCCESS;
}

HANDLE LaunchInteractiveHelper(DWORD clientProcessId) {
    EnablePrivilege(SE_ASSIGNPRIMARYTOKEN_NAME);
    EnablePrivilege(SE_INCREASE_QUOTA_NAME);
    DWORD targetSessionId = 0;
    if (!ProcessIdToSessionId(clientProcessId, &targetSessionId)) ThrowWin32("ProcessIdToSessionId(client)");
    struct HandleCloser { HANDLE value; ~HandleCloser() { if (value) CloseHandle(value); } };
    HANDLE clientProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, clientProcessId);
    if (!clientProcess) ThrowWin32("OpenProcess(client for helper token)");
    HandleCloser clientProcessCloser{clientProcess};
    HANDLE rawToken = nullptr;
    if (!OpenProcessToken(clientProcess, TOKEN_DUPLICATE | TOKEN_QUERY, &rawToken))
        ThrowWin32("OpenProcessToken(client)");
    HandleCloser tokenCloser{rawToken};
    HANDLE primary = nullptr;
    constexpr DWORD primaryAccess = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY |
        TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;
    if (!DuplicateTokenEx(rawToken, primaryAccess, nullptr, SecurityImpersonation, TokenPrimary, &primary))
        ThrowWin32("DuplicateTokenEx");
    HandleCloser primaryCloser{primary};
    DWORD tokenSessionId = 0;
    DWORD returnedBytes = 0;
    if (!GetTokenInformation(primary, TokenSessionId, &tokenSessionId, sizeof(tokenSessionId), &returnedBytes))
        ThrowWin32("GetTokenInformation(TokenSessionId)");
    if (tokenSessionId != targetSessionId)
        ThrowWin32("Interactive token session mismatch", ERROR_INVALID_DATA);
    LPVOID environment = nullptr;
    if (!CreateEnvironmentBlock(&environment, primary, FALSE)) ThrowWin32("CreateEnvironmentBlock");
    struct EnvironmentCloser { LPVOID value; ~EnvironmentCloser() { if (value) DestroyEnvironmentBlock(value); } } environmentCloser{environment};
    const std::wstring executable = ExecutablePath();
    std::wstring command = L"\"" + executable + L"\" --helper --client-pid " + std::to_wstring(clientProcessId);
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    startup.lpDesktop = const_cast<LPWSTR>(L"winsta0\\default");
    PROCESS_INFORMATION process{};
    if (!CreateProcessAsUserW(primary, executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, environment,
        std::filesystem::path(executable).parent_path().c_str(), &startup, &process)) ThrowWin32("CreateProcessAsUserW(helper)");
    CloseHandle(process.hThread);
    Diagnostics::Write(L"Elevated interactive client token assigned to native helper in session " +
        std::to_wstring(targetSessionId) + L".");
    return process.hProcess;
}

void WINAPI ServiceMain(DWORD argumentCount, LPWSTR* arguments) {
    statusHandle = RegisterServiceCtrlHandlerExW(ServiceName, ServiceHandler, nullptr);
    if (!statusHandle) return;
    ReportStatus(SERVICE_START_PENDING, 0, ERROR_SUCCESS, 15'000);
    HANDLE client = nullptr;
    HANDLE sessionMonitor = nullptr;
    try {
        serviceClientProcessId = 0;
        for (DWORD index = 1; index + 1 < argumentCount; ++index) {
            if (_wcsicmp(arguments[index], L"--client-pid") == 0)
                serviceClientProcessId = wcstoul(arguments[index + 1], nullptr, 10);
        }
        if (serviceClientProcessId == 0) throw std::runtime_error("Service client process id is missing");
        stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!stopEvent) ThrowWin32("CreateEventW(service stop)");
        reconcileEvent = CreateEventW(nullptr, FALSE, TRUE, nullptr);
        if (!reconcileEvent) ThrowWin32("CreateEventW(session reconcile)");
        client = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, serviceClientProcessId);
        if (!client) ThrowWin32("OpenProcess(service client)");
        sessionMonitor = CreateThread(nullptr, 0, SessionNotificationThread, nullptr, 0, nullptr);
        if (!sessionMonitor) ThrowWin32("CreateThread(WTS monitor)");
        Diagnostics::Write(L"Native service started WTS active-session monitor. ClientPid=" +
            std::to_wstring(serviceClientProcessId) + L".");
        ReportStatus(SERVICE_RUNNING, SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SESSIONCHANGE);
        HANDLE waits[]{stopEvent, client};
        const DWORD reason = WaitForMultipleObjects(ARRAYSIZE(waits), waits, FALSE, INFINITE);
        Diagnostics::Write(L"Native service is stopping. WaitReason=" + std::to_wstring(reason) + L".");
        SetEvent(stopEvent);
        WaitForSingleObject(sessionMonitor, 5'000);
        CloseHandle(sessionMonitor);
        sessionMonitor = nullptr;
        StopHelper();
        CloseHandle(reconcileEvent);
        reconcileEvent = nullptr;
        CloseHandle(client);
        client = nullptr;
        CloseHandle(stopEvent);
        stopEvent = nullptr;
        ReportStatus(SERVICE_STOPPED, 0);
    } catch (const std::exception& error) {
        const std::string message(error.what());
        Diagnostics::Write(L"Native service failed: " + std::wstring(message.begin(), message.end()));
        if (stopEvent) SetEvent(stopEvent);
        if (sessionMonitor) {
            WaitForSingleObject(sessionMonitor, 5'000);
            CloseHandle(sessionMonitor);
        }
        StopHelper();
        if (client) CloseHandle(client);
        if (stopEvent) CloseHandle(stopEvent);
        if (reconcileEvent) CloseHandle(reconcileEvent);
        stopEvent = nullptr;
        reconcileEvent = nullptr;
        ReportStatus(SERVICE_STOPPED, 0, ERROR_SERVICE_SPECIFIC_ERROR);
    }
}
}

NativeRuntime::~NativeRuntime() { Stop(); }

void NativeRuntime::StartForCurrentClient() {
    const std::wstring current = ExecutablePath();
    wchar_t programFiles[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"ProgramFiles", programFiles, ARRAYSIZE(programFiles)))
        ThrowWin32("GetEnvironmentVariableW(ProgramFiles)");
    const std::filesystem::path directory = std::filesystem::path(programFiles) / L"RotaLink" / L"Runtime" / L"1.2.0-native.3";
    std::filesystem::create_directories(directory);
    const std::filesystem::path installed = directory / L"RotaLink.exe";
    installedRuntime_ = installed;
    try {
        manager_ = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        if (!manager_) ThrowWin32("OpenSCManagerW");
        service_ = OpenServiceW(manager_, ServiceName, ServiceAccess);
        if (service_) StopServiceIfNeeded(service_);
        else if (GetLastError() != ERROR_SERVICE_DOES_NOT_EXIST) ThrowWin32("OpenServiceW");
        const std::filesystem::path temporary = directory / L"RotaLink.exe.new";
        std::filesystem::copy_file(current, temporary, std::filesystem::copy_options::overwrite_existing);
        if (!MoveFileExW(temporary.c_str(), installed.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            ThrowWin32("MoveFileExW(runtime)");
        const std::wstring binaryPath = L"\"" + installed.wstring() + L"\" --service";
        if (!service_) {
            service_ = CreateServiceW(manager_, ServiceName, ServiceDisplayName, ServiceAccess,
                SERVICE_WIN32_OWN_PROCESS, SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL, binaryPath.c_str(),
                nullptr, nullptr, nullptr, nullptr, nullptr);
            if (!service_) ThrowWin32("CreateServiceW");
        } else if (!ChangeServiceConfigW(service_, SERVICE_WIN32_OWN_PROCESS, SERVICE_DEMAND_START,
            SERVICE_ERROR_NORMAL, binaryPath.c_str(), nullptr, nullptr, nullptr, nullptr, nullptr, ServiceDisplayName))
            ThrowWin32("ChangeServiceConfigW");
        std::wstring processId = std::to_wstring(GetCurrentProcessId());
        const wchar_t* arguments[]{L"--client-pid", processId.c_str()};
        if (!StartServiceW(service_, ARRAYSIZE(arguments), arguments) && GetLastError() != ERROR_SERVICE_ALREADY_RUNNING)
            ThrowWin32("StartServiceW");
        if (!WaitForState(service_, SERVICE_RUNNING, 10'000)) throw std::runtime_error("Native control service did not reach RUNNING state");
        Diagnostics::Write(L"Native SYSTEM control runtime is running from Program Files.");
    } catch (...) {
        Stop();
        throw;
    }
}

void NativeRuntime::Stop() noexcept {
    if (service_) {
        StopServiceIfNeeded(service_);
        if (!DeleteService(service_) && GetLastError() != ERROR_SERVICE_MARKED_FOR_DELETE) {
            Diagnostics::Write(L"Native control service registration could not be removed. Win32=" +
                std::to_wstring(GetLastError()) + L".");
        }
        CloseServiceHandle(service_);
        service_ = nullptr;
    }
    if (manager_) {
        CloseServiceHandle(manager_);
        manager_ = nullptr;
    }
    if (!installedRuntime_.empty()) {
        if (!DeleteFileW(installedRuntime_.c_str()) && GetLastError() != ERROR_FILE_NOT_FOUND) {
            MoveFileExW(installedRuntime_.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        const std::filesystem::path versionDirectory = installedRuntime_.parent_path();
        RemoveDirectoryW(versionDirectory.c_str());
        RemoveDirectoryW(versionDirectory.parent_path().c_str());
        RemoveDirectoryW(versionDirectory.parent_path().parent_path().c_str());
        installedRuntime_.clear();
    }
}

int NativeRuntime::RunServiceMode() {
    SERVICE_TABLE_ENTRYW table[]{{const_cast<LPWSTR>(ServiceName), ServiceMain}, {nullptr, nullptr}};
    if (!StartServiceCtrlDispatcherW(table)) return static_cast<int>(GetLastError());
    return 0;
}

int NativeRuntime::RunHelperMode(DWORD allowedClientProcessId) {
    if (allowedClientProcessId == 0) return 30;
    Diagnostics::Initialize();
    return InputPipeServer(allowedClientProcessId).Run();
}
