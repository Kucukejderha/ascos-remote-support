$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$matrixPath = Join-Path $PSScriptRoot "windows-compatibility-matrix.json"
$validator = Join-Path $root "scripts\Test-RotaLinkCompatibilityMatrix.ps1"
$results = Join-Path $root "work\compatibility-matrix-selftest"
if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
New-Item -ItemType Directory -Force -Path $results | Out-Null

$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
foreach ($target in $matrix.targets) {
    $report = [pscustomobject][ordered]@{
        schemaVersion = [int]$matrix.schemaVersion
        targetId = [string]$target.id
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        status = "Pass"
        machine = [ordered]@{ computerName = "SELFTEST-" + [string]$target.id }
        os = [ordered]@{
            caption = [string]$target.displayName
            major = [int]$target.major
            minor = [int]$target.minor
            build = [int]$target.minimumBuild
            server = [bool]$target.server
        }
        checks = @([pscustomobject][ordered]@{ id="selftest"; severity="P0"; passed=$true; message="synthetic pass" })
    }
    $path = Join-Path $results ("rotalink-compatibility-" + [string]$target.id + ".json")
    [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 7), (New-Object Text.UTF8Encoding($false)))
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -ResultsDirectory $results -MatrixPath $matrixPath
if ($LASTEXITCODE -ne 0) { throw "Complete compatibility matrix should pass; exit code $LASTEXITCODE." }
$summary = Get-Content -LiteralPath (Join-Path $results "matrix-summary.json") -Raw | ConvertFrom-Json
if ([string]$summary.status -ne "Pass" -or [int]$summary.passed -ne @($matrix.targets).Count) {
    throw "Compatibility matrix self-test produced an invalid summary."
}

$failedReportPath = Join-Path $results "rotalink-compatibility-server-2019.json"
$failedReport = Get-Content -LiteralPath $failedReportPath -Raw | ConvertFrom-Json
$failedReport.status = "Fail"
[IO.File]::WriteAllText($failedReportPath, ($failedReport | ConvertTo-Json -Depth 7), (New-Object Text.UTF8Encoding($false)))
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -ResultsDirectory $results -MatrixPath $matrixPath
if ($LASTEXITCODE -ne 2) { throw "P0 failure must close the release gate with exit code 2." }

Write-Host "Compatibility matrix pass/fail gate checks passed."
