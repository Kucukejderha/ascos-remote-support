param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$BuildDirectory = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) { $BuildDirectory = Join-Path $root "native\out" }
$cmake = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    throw "CMake bulunamadı. Derleme makinesine Visual Studio C++ Desktop workload ve CMake kurulmalıdır; müşteri bilgisayarına hiçbir araç kurulmaz."
}
$windowsSdk = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\Include",
    "$env:ProgramFiles\Windows Kits\10\Include"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace([string]$windowsSdk)) {
    throw "Windows 10/11 SDK bulunamadı. Bu yalnız derleme makinesinin gereksinimidir."
}
& $cmake.Source -S (Join-Path $root "native") -B $BuildDirectory -A x64
if ($LASTEXITCODE -ne 0) { throw "Native CMake yapılandırması başarısız oldu." }
& $cmake.Source --build $BuildDirectory --config $Configuration --target RotaLink.Client
if ($LASTEXITCODE -ne 0) { throw "Native RotaLink derlemesi başarısız oldu." }
$artifact = Join-Path $BuildDirectory "$Configuration\RotaLink.exe"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-NativeClientArtifact.ps1") `
    -Path $artifact -OutputPath (Join-Path $root "artifacts\native-client-report.json")
if ($LASTEXITCODE -ne 0) { throw "Native RotaLink bağımlılık veya boyut kapısını geçemedi." }
Write-Host "Native RotaLink: $artifact"
