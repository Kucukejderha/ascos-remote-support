param(
    [string]$NativeClientPath = "",
    [string]$Version = "1.2.0-native.8"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$kitDirectory = Join-Path $artifacts ("RotaLink-" + $Version + "-Uyumluluk-Test-Kiti")
$zipPath = $kitDirectory + ".zip"
$clientName = "RotaLink-" + $Version + ".exe"

if ([string]::IsNullOrWhiteSpace($NativeClientPath)) {
    $NativeClientPath = Join-Path $root "native\out\Release\RotaLink.exe"
}
$client = [IO.Path]::GetFullPath($NativeClientPath)
if (-not (Test-Path -LiteralPath $client)) {
    throw "Native client not found: $client. Build it with scripts\build-native-client.ps1 or pass -NativeClientPath."
}

$verification = Join-Path $artifacts "native-client-kit-report.json"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-NativeClientArtifact.ps1") -Path $client -OutputPath $verification
if ($LASTEXITCODE -ne 0) { throw "Native client PE verification failed." }

if (Test-Path -LiteralPath $kitDirectory) { Remove-Item -LiteralPath $kitDirectory -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Force -Path $kitDirectory | Out-Null

$files = @(
    @{ Source=$client; Name=$clientName },
    @{ Source=$verification; Name="native-client-report.json" },
    @{ Source=(Join-Path $PSScriptRoot "Test-RotaLinkCompatibility.ps1"); Name="Test-RotaLinkCompatibility.ps1" },
    @{ Source=(Join-Path $PSScriptRoot "Test-RotaLinkCompatibilityMatrix.ps1"); Name="Test-RotaLinkCompatibilityMatrix.ps1" },
    @{ Source=(Join-Path $PSScriptRoot "RotaLink-Uyumluluk-Testi.cmd"); Name="RotaLink-Uyumluluk-Testi.cmd" },
    @{ Source=(Join-Path $root "tests\windows-compatibility-matrix.json"); Name="windows-compatibility-matrix.json" },
    @{ Source=(Join-Path $root "docs\WINDOWS-TEST-LABORATUVARI.tr.md"); Name="WINDOWS-TEST-LABORATUVARI.tr.md" },
    @{ Source=(Join-Path $root "docs\WINDOWS-UYUMLULUK-YOL-HARITASI.tr.md"); Name="WINDOWS-UYUMLULUK-YOL-HARITASI.tr.md" }
)
foreach ($file in $files) {
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $kitDirectory $file.Name) -Force
}

$hashLines = @()
foreach ($file in Get-ChildItem -LiteralPath $kitDirectory -File | Sort-Object Name) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    $hashLines += $hash + "  " + $file.Name
}
[IO.File]::WriteAllLines((Join-Path $kitDirectory "SHA256SUMS.txt"), $hashLines, (New-Object Text.UTF8Encoding($false)))
$kitFiles = Get-ChildItem -LiteralPath $kitDirectory -File | ForEach-Object { $_.FullName }
Compress-Archive -Path $kitFiles -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
Write-Host "Compatibility kit: $zipPath"
Write-Host "Size: $((Get-Item -LiteralPath $zipPath).Length) bytes"
Write-Host "SHA-256: $zipHash"
