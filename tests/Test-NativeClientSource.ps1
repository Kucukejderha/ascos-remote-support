$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cmake = Get-Content -LiteralPath (Join-Path $root "native\CMakeLists.txt") -Raw
$main = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\main.cpp") -Raw
$compatibility = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\PlatformCompatibility.cpp") -Raw
$window = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\NativeWindow.cpp") -Raw
$manifest = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\app.manifest") -Raw
$probe = Get-Content -LiteralPath (Join-Path $root "scripts\Test-RotaLinkCompatibility.ps1") -Raw
$signaling = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\SignalingClient.cpp") -Raw
$identity = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\CngDeviceIdentity.cpp") -Raw
$sessionRuntime = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\SessionRuntime.cpp") -Raw
$inputEngine = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\NativeInputEngine.cpp") -Raw
$gdiCapture = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\GdiJpegCapture.cpp") -Raw
$runtime = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\NativeRuntime.cpp") -Raw
$inputPipe = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\InputPipe.cpp") -Raw
$diagnostics = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\Diagnostics.cpp") -Raw

$checks = @(
    @{ Id="static-crt"; Passed=$cmake.Contains('CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded') },
    @{ Id="minimum-win8-api"; Passed=$cmake.Contains('_WIN32_WINNT=0x0602') },
    @{ Id="native-gui-target"; Passed=$cmake.Contains('add_executable(RotaLink.Client WIN32') },
    @{ Id="customer-exe-name"; Passed=$cmake.Contains('OUTPUT_NAME "RotaLink"') },
    @{ Id="release-size-optimization"; Passed=($cmake.Contains('/O1') -and $cmake.Contains('/OPT:REF') -and $cmake.Contains('/OPT:ICF')) },
    @{ Id="reproducible-build"; Passed=$cmake.Contains('/Brepro') },
    @{ Id="single-instance"; Passed=($main.Contains('CreateMutexW') -and $main.Contains('if (ActivateExistingWindow()) return 0;')) },
    @{ Id="explicit-uac-elevation"; Passed=($main.Contains('ShellExecuteExW') -and $main.Contains('lpVerb = L"runas"')) },
    @{ Id="runtime-os-version"; Passed=$compatibility.Contains('RtlGetVersion') },
    @{ Id="server-core-block"; Passed=$compatibility.Contains('serverCore') },
    @{ Id="dynamic-modern-dpi"; Passed=$window.Contains('GetProcAddress(user32, "GetDpiForWindow")') },
    @{ Id="per-monitor-manifest"; Passed=$manifest.Contains('PerMonitorV2,PerMonitor,System') },
    @{ Id="no-managed-project"; Passed=(-not (Test-Path (Join-Path $root "native\RotaLink.Client\RotaLink.Client.csproj"))) }
    @{ Id="dotnet-not-a-release-gate"; Passed=($probe.Contains('Add-Check "native-runtime" "P0" $true') -and -not $probe.Contains('Add-Check "dotnet-48"')) },
    @{ Id="native-diagnostic-evidence"; Passed=($probe.Contains('schemaVersion = 2') -and $probe.Contains('Get-FileHash') -and $probe.Contains('RotaLink-Native.log') -and $probe.Contains('RotaLinkNativeRuntime')) },
    @{ Id="portable-unified-log"; Passed=($diagnostics.Contains('ExecutableDirectory()') -and $diagnostics.Contains('RotaLink-Native.log') -and $runtime.Contains('--log-directory') -and $probe.Contains('portableLog')) },
    @{ Id="cng-p256-spki"; Passed=($identity.Contains('BCRYPT_ECDSA_P256_ALGORITHM') -and $identity.Contains('BCRYPT_ECCPUBLIC_BLOB')) },
    @{ Id="challenge-signature"; Passed=($signaling.Contains('SignBase64(nonce)') -and $signaling.Contains('/verify')) },
    @{ Id="support-code"; Passed=($signaling.Contains('/v1/support-codes') -and $signaling.Contains('result.code.size() != 9')) },
    @{ Id="split-control-video"; Passed=($sessionRuntime.Contains('ConnectHostSocket(session, "control")') -and $sessionRuntime.Contains('ConnectHostSocket(session, "video")')) },
    @{ Id="dynamic-input-desktop"; Passed=($inputEngine.Contains('OpenInputDesktop') -and $inputEngine.Contains('SetThreadDesktop')) },
    @{ Id="same-desktop-handle-reuse"; Passed=($inputEngine.Contains('GetThreadDesktop') -and $inputEngine.Contains('_wcsicmp(currentName.c_str(), nextName.c_str())') -and $inputEngine.Contains('CloseDesktop(next)')) },
    @{ Id="single-exe-service-helper"; Passed=($main.Contains('--service') -and $main.Contains('--helper') -and $runtime.Contains('CreateServiceW') -and $runtime.Contains('CreateProcessAsUserW')) },
    @{ Id="interactive-user-token"; Passed=($runtime.Contains('OpenProcess(client for helper token)') -and $runtime.Contains('OpenProcessToken(client)') -and $runtime.Contains('Interactive token session mismatch') -and $runtime.Contains('TOKEN_ASSIGN_PRIMARY') -and -not $runtime.Contains('SetMediumIntegrity(primary)') -and $runtime.Contains('winsta0\\default')) },
    @{ Id="wts-active-session-monitor"; Passed=($runtime.Contains('WTSQuerySessionInformationW') -and $runtime.Contains('WTSConnectState') -and $runtime.Contains('WTSActive')) },
    @{ Id="authenticated-input-ipc"; Passed=($inputPipe.Contains('GetNamedPipeClientProcessId') -and $inputPipe.Contains('GetNamedPipeClientSessionId') -and $inputPipe.Contains('PIPE_REJECT_REMOTE_CLIENTS') -and $inputPipe.Contains('kernel-identity-ok') -and -not $inputPipe.Contains('OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION') -and $sessionRuntime.Contains('InputPipeClient input')) },
    @{ Id="helper-clean-stop"; Passed=($runtime.Contains('HelperStop') -and $inputPipe.Contains('WaitForSingleObject(stop') -and $inputEngine.Contains('KEYEVENTF_KEYUP')) },
    @{ Id="portable-runtime-cleanup"; Passed=($runtime.Contains('DeleteFileW(installedRuntime_') -and $runtime.Contains('RemoveDirectoryW(versionDirectory')) },
    @{ Id="atomic-sendinput"; Passed=($inputEngine.Contains('SendInput(') -and $inputEngine.Contains('sent == expected')) },
    @{ Id="physical-click-cadence"; Passed=($inputEngine.Contains('SendPhysicalClick') -and $inputEngine.Contains('Sleep(16)') -and $inputEngine.Contains('Sleep(32)') -and $inputEngine.Contains('WindowFromPoint') -and $inputEngine.Contains('native-physical-click-ok')) },
    @{ Id="truthful-input-result"; Passed=($inputPipe.Contains('result.accepted ? "true" : "false"') -and $inputEngine.Contains('result.accepted = Send(inputs, result)') -and $sessionRuntime.Contains('native-helper-ipc-unavailable')) },
    @{ Id="native-dxgi-video"; Passed=($sessionRuntime.Contains('DesktopDuplicator duplicator') -and $sessionRuntime.Contains('H264Encoder encoder') -and $sessionRuntime.Contains('socket.SendBinary(packet)')) },
    @{ Id="native-jpeg-fallback"; Passed=($sessionRuntime.Contains('GdiVideoLoop(socket)') -and $gdiCapture.Contains('GUID_ContainerFormatJpeg') -and $gdiCapture.Contains('StretchBlt')) },
    @{ Id="multi-monitor-geometry"; Passed=($sessionRuntime.Contains('SM_CMONITORS') -and $gdiCapture.Contains('SM_XVIRTUALSCREEN') -and $gdiCapture.Contains('SM_CXVIRTUALSCREEN')) }
)
$failed = @($checks | Where-Object { -not $_.Passed })
$checks | ForEach-Object { [pscustomobject]@{ id=$_.Id; passed=[bool]$_.Passed } } | Format-Table -AutoSize
if ($failed.Count -gt 0) { throw "Native client source gate failed: " + (($failed | ForEach-Object Id) -join ", ") }
Write-Host "Native client source and compatibility gates passed."
