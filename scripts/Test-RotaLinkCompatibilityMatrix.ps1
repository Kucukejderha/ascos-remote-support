param(
    [string]$ResultsDirectory = "",
    [string]$MatrixPath = "",
    [string]$SummaryPath = "",
    [switch]$AllowIncomplete
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $ResultsDirectory = Join-Path $root "work\compatibility-results" }
if ([string]::IsNullOrWhiteSpace($MatrixPath)) { $MatrixPath = Join-Path $root "tests\windows-compatibility-matrix.json" }
if ([string]::IsNullOrWhiteSpace($SummaryPath)) { $SummaryPath = Join-Path $ResultsDirectory "matrix-summary.json" }

$matrix = Get-Content -LiteralPath $MatrixPath -Raw | ConvertFrom-Json
$reports = @{}
if (Test-Path -LiteralPath $ResultsDirectory) {
    foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter "rotalink-compatibility-*.json" -File) {
        $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([int]$report.schemaVersion -ne [int]$matrix.schemaVersion) { throw "Schema mismatch in $($file.Name)." }
        if ($reports.ContainsKey([string]$report.targetId)) { throw "Duplicate target report: $($report.targetId)." }
        $reports[[string]$report.targetId] = $report
    }
}

$results = @()
foreach ($target in $matrix.targets) {
    $id = [string]$target.id
    if (-not $reports.ContainsKey($id)) {
        $results += [pscustomobject][ordered]@{ id=$id; displayName=$target.displayName; status="Missing"; reason="Report not found" }
        continue
    }
    $report = $reports[$id]
    $reasons = @()
    $actualServer = [bool]$report.os.server
    if ($actualServer -ne [bool]$target.server) { $reasons += "Client/server product type mismatch" }
    if ([int]$report.os.major -ne [int]$target.major -or [int]$report.os.minor -ne [int]$target.minor) { $reasons += "OS version mismatch" }
    if ([int]$report.os.build -lt [int]$target.minimumBuild) { $reasons += "Build is below minimum" }
    if ($null -ne $target.maximumBuild -and [int]$report.os.build -gt [int]$target.maximumBuild) { $reasons += "Build is above target range" }
    if ([string]$report.status -eq "Fail") { $reasons += "One or more P0 checks failed" }
    $resultStatus = if ($reasons.Count -gt 0) { "Fail" } else { [string]$report.status }
    $results += [pscustomobject][ordered]@{
        id = $id
        displayName = [string]$target.displayName
        supportLevel = [string]$target.supportLevel
        status = $resultStatus
        machine = [string]$report.machine.computerName
        os = [string]$report.os.caption
        build = [int]$report.os.build
        reason = ($reasons -join "; ")
    }
}

$missing = @($results | Where-Object { $_.status -eq "Missing" }).Count
$failed = @($results | Where-Object { $_.status -eq "Fail" }).Count
$warned = @($results | Where-Object { $_.status -eq "Warn" }).Count
$overall = if ($failed -gt 0) { "Fail" } elseif ($missing -gt 0 -and -not $AllowIncomplete) { "Incomplete" } elseif ($warned -gt 0 -or $missing -gt 0) { "Warn" } else { "Pass" }
$summary = [pscustomobject][ordered]@{
    schemaVersion = [int]$matrix.schemaVersion
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    status = $overall
    targetCount = @($matrix.targets).Count
    passed = @($results | Where-Object { $_.status -eq "Pass" }).Count
    warned = $warned
    failed = $failed
    missing = $missing
    results = $results
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryPath) | Out-Null
$json = $summary | ConvertTo-Json -Depth 7
[IO.File]::WriteAllText([IO.Path]::GetFullPath($SummaryPath), $json, (New-Object Text.UTF8Encoding($false)))
$results | Format-Table id, status, build, machine, reason -AutoSize
Write-Host "Matrix status: $overall"
Write-Host "Summary: $([IO.Path]::GetFullPath($SummaryPath))"
if ($overall -eq "Fail" -or $overall -eq "Incomplete") { exit 2 }
exit 0
