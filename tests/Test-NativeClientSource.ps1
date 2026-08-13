$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cmake = Get-Content -LiteralPath (Join-Path $root "native\CMakeLists.txt") -Raw
$main = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\main.cpp") -Raw
$compatibility = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\PlatformCompatibility.cpp") -Raw
$window = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\NativeWindow.cpp") -Raw
$manifest = Get-Content -LiteralPath (Join-Path $root "native\RotaLink.Client\app.manifest") -Raw
$probe = Get-Content -LiteralPath (Join-Path $root "scripts\Test-RotaLinkCompatibility.ps1") -Raw

$checks = @(
    @{ Id="static-crt"; Passed=$cmake.Contains('CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded') },
    @{ Id="minimum-win8-api"; Passed=$cmake.Contains('_WIN32_WINNT=0x0602') },
    @{ Id="native-gui-target"; Passed=$cmake.Contains('add_executable(RotaLink.Client WIN32') },
    @{ Id="single-instance"; Passed=$main.Contains('CreateMutexW') },
    @{ Id="runtime-os-version"; Passed=$compatibility.Contains('RtlGetVersion') },
    @{ Id="server-core-block"; Passed=$compatibility.Contains('serverCore') },
    @{ Id="dynamic-modern-dpi"; Passed=$window.Contains('GetProcAddress(user32, "GetDpiForWindow")') },
    @{ Id="per-monitor-manifest"; Passed=$manifest.Contains('PerMonitorV2,PerMonitor,System') },
    @{ Id="no-managed-project"; Passed=(-not (Test-Path (Join-Path $root "native\RotaLink.Client\RotaLink.Client.csproj"))) }
    @{ Id="dotnet-not-a-release-gate"; Passed=($probe.Contains('Add-Check "native-runtime" "P0" $true') -and -not $probe.Contains('Add-Check "dotnet-48"')) }
)
$failed = @($checks | Where-Object { -not $_.Passed })
$checks | ForEach-Object { [pscustomobject]@{ id=$_.Id; passed=[bool]$_.Passed } } | Format-Table -AutoSize
if ($failed.Count -gt 0) { throw "Native client source gate failed: " + (($failed | ForEach-Object Id) -join ", ") }
Write-Host "Native client source and compatibility gates passed."
