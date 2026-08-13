param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int64]$MaximumBytes = 10MB,
    [string]$OutputPath = ""
)
$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
$bytes = [IO.File]::ReadAllBytes($resolved)
if ($bytes.Length -lt 512 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Not a valid PE image." }
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ($peOffset -lt 0 -or $peOffset + 264 -gt $bytes.Length -or
    $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45) { throw "Invalid PE header." }
$machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
$optional = $peOffset + 24
$magic = [BitConverter]::ToUInt16($bytes, $optional)
if ($magic -ne 0x20B) { throw "RotaLink native client must be PE32+ x64." }
$clrDirectory = $optional + 112 + (14 * 8)
$clrRva = [BitConverter]::ToUInt32($bytes, $clrDirectory)
$clrSize = [BitConverter]::ToUInt32($bytes, $clrDirectory + 4)
$checks = @(
    [pscustomobject][ordered]@{ id="x64"; passed=($machine -eq 0x8664); actual=("0x{0:X4}" -f $machine) },
    [pscustomobject][ordered]@{ id="no-clr"; passed=($clrRva -eq 0 -and $clrSize -eq 0); actual=("RVA={0},Size={1}" -f $clrRva,$clrSize) },
    [pscustomobject][ordered]@{ id="maximum-size"; passed=($bytes.Length -le $MaximumBytes); actual=$bytes.Length }
)
$status = if (@($checks | Where-Object { -not $_.passed }).Count -eq 0) { "Pass" } else { "Fail" }
$report = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    status = $status
    path = $resolved
    bytes = $bytes.Length
    maximumBytes = $MaximumBytes
    sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    checks = $checks
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($report | ConvertTo-Json -Depth 5),
        (New-Object Text.UTF8Encoding($false)))
}
$report | Format-List status, bytes, maximumBytes, sha256
$checks | Format-Table id, passed, actual -AutoSize
if ($status -ne "Pass") { exit 2 }
