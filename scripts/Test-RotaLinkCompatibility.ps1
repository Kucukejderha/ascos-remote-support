param(
    [Parameter(Mandatory = $true)][string]$TargetId,
    [string]$OutputPath = "",
    [string]$ServerBaseUrl = "https://45.87.173.201.nip.io",
    [string]$ClientPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Read-RegistryValue {
    param([string]$Path, [string]$Name)
    try {
        $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($Path)
        if ($null -eq $key) { return $null }
        try { return $key.GetValue($Name, $null) } finally { $key.Dispose() }
    } catch { return $null }
}

function Test-Endpoint {
    param([string]$BaseUrl)
    $started = [DateTime]::UtcNow
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $request = [Net.HttpWebRequest]::Create(($BaseUrl.TrimEnd('/') + "/health"))
        $request.Method = "GET"
        $request.Timeout = 10000
        $request.ReadWriteTimeout = 10000
        $request.UserAgent = "RotaLink-Compatibility-Probe/1"
        $response = $request.GetResponse()
        try {
            return [ordered]@{
                reachable = $true
                statusCode = [int]$response.StatusCode
                elapsedMs = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
                error = ""
            }
        } finally { $response.Dispose() }
    } catch {
        return [ordered]@{
            reachable = $false
            statusCode = 0
            elapsedMs = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
            error = $_.Exception.Message
        }
    }
}

function Add-Check {
    param([string]$Id, [string]$Severity, [bool]$Passed, [string]$Message)
    $script:checks += [pscustomobject][ordered]@{
        id = $Id
        severity = $Severity
        passed = $Passed
        message = $Message
    }
}

function Read-LogTail {
    param([string]$Path, [int]$Lines = 200)
    $result = [ordered]@{ path=$Path; exists=$false; lastWriteTimeUtc=""; tail=@(); error="" }
    try {
        if (Test-Path -LiteralPath $Path) {
            $file = Get-Item -LiteralPath $Path
            $result.exists = $true
            $result.lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString("o")
            $result.tail = @(Get-Content -LiteralPath $Path -Tail $Lines -ErrorAction Stop)
        }
    } catch { $result.error = $_.Exception.Message }
    return [pscustomobject]$result
}

if ([string]::IsNullOrWhiteSpace($ClientPath)) {
    $candidate = Get-ChildItem -LiteralPath $PSScriptRoot -Filter "RotaLink-*.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $ClientPath = $candidate.FullName }
}
$clientEvidence = [ordered]@{
    found = $false
    path = ""
    fileVersion = ""
    productVersion = ""
    bytes = 0
    sha256 = ""
    lastWriteTimeUtc = ""
    error = ""
}
if (-not [string]::IsNullOrWhiteSpace($ClientPath)) {
    try {
        $resolvedClient = [IO.Path]::GetFullPath($ClientPath)
        $clientFile = Get-Item -LiteralPath $resolvedClient -ErrorAction Stop
        $clientEvidence.found = $true
        $clientEvidence.path = $resolvedClient
        $clientEvidence.fileVersion = [string]$clientFile.VersionInfo.FileVersion
        $clientEvidence.productVersion = [string]$clientFile.VersionInfo.ProductVersion
        $clientEvidence.bytes = [long]$clientFile.Length
        $clientEvidence.sha256 = (Get-FileHash -LiteralPath $resolvedClient -Algorithm SHA256).Hash.ToLowerInvariant()
        $clientEvidence.lastWriteTimeUtc = $clientFile.LastWriteTimeUtc.ToString("o")
    } catch { $clientEvidence.error = $_.Exception.Message }
}

$checks = @()
$currentVersionPath = "SOFTWARE\Microsoft\Windows NT\CurrentVersion"
$productName = [string](Read-RegistryValue $currentVersionPath "ProductName")
$installationType = Read-RegistryValue $currentVersionPath "InstallationType"
if ([string]::IsNullOrWhiteSpace([string]$installationType)) { $installationType = "Unknown" }
$buildText = [string](Read-RegistryValue $currentVersionPath "CurrentBuildNumber")
if ([string]::IsNullOrWhiteSpace($buildText)) { $buildText = [string](Read-RegistryValue $currentVersionPath "CurrentBuild") }
$buildNumber = 0
[void][int]::TryParse($buildText, [ref]$buildNumber)
$currentVersionText = [string](Read-RegistryValue $currentVersionPath "CurrentVersion")
$major = 0
$minor = 0
if ($buildNumber -ge 10240) {
    $major = 10
    $minor = 0
} else {
    $versionParts = $currentVersionText.Split('.')
    if ($versionParts.Length -ge 2) {
        [void][int]::TryParse($versionParts[0], [ref]$major)
        [void][int]::TryParse($versionParts[1], [ref]$minor)
    }
}
$version = New-Object Version $major, $minor, $buildNumber
$isServer = $productName.IndexOf("Server", [StringComparison]::OrdinalIgnoreCase) -ge 0
$productType = if ($isServer) { 3 } else { 1 }
$normalizedProductName = if (-not $isServer -and $major -eq 10 -and $buildNumber -ge 22000) {
    "Windows 11"
} elseif (-not $isServer -and $major -eq 10) {
    "Windows 10"
} elseif ($isServer -and $major -eq 6 -and $minor -eq 2) {
    "Windows Server 2012"
} elseif ($isServer -and $major -eq 6 -and $minor -eq 3) {
    "Windows Server 2012 R2"
} elseif ($isServer -and $buildNumber -eq 14393) {
    "Windows Server 2016"
} elseif ($isServer -and $buildNumber -eq 17763) {
    "Windows Server 2019"
} elseif ($isServer -and $buildNumber -eq 20348) {
    "Windows Server 2022"
} elseif ($isServer -and $buildNumber -ge 26100) {
    "Windows Server 2025 veya üstü"
} else {
    $productName
}
$servicePack = [string](Read-RegistryValue $currentVersionPath "CSDVersion")
$computer = [pscustomobject][ordered]@{
    Manufacturer = "Unknown"
    Model = "Unknown"
    Domain = [string]$env:USERDOMAIN
}
try {
    $wmiComputer = Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
    $computer = $wmiComputer
} catch { }
$frameworkReleaseValue = Read-RegistryValue "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
$frameworkRelease = if ($null -eq $frameworkReleaseValue) { 0 } else { [int]$frameworkReleaseValue }
$is64Bit = [Environment]::Is64BitOperatingSystem
$isCore = ([string]$installationType).IndexOf("Core", [StringComparison]::OrdinalIgnoreCase) -ge 0
$interactive = [Environment]::UserInteractive
$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $identityName = $identity.Name
} finally { $identity.Dispose() }

$screens = @()
$dpiX = 0
$dpiY = 0
$displayError = ""
try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    foreach ($screen in [Windows.Forms.Screen]::AllScreens) {
        $screens += [pscustomobject][ordered]@{
            deviceName = $screen.DeviceName
            primary = $screen.Primary
            x = $screen.Bounds.X
            y = $screen.Bounds.Y
            width = $screen.Bounds.Width
            height = $screen.Bounds.Height
        }
    }
    $graphics = [Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    try { $dpiX = [int]$graphics.DpiX; $dpiY = [int]$graphics.DpiY } finally { $graphics.Dispose() }
} catch { $displayError = $_.Exception.Message }

$gpus = @()
try {
    foreach ($gpu in @(Get-WmiObject -Class Win32_VideoController -ErrorAction Stop)) {
        $gpus += [pscustomobject][ordered]@{
            name = [string]$gpu.Name
            driverVersion = [string]$gpu.DriverVersion
            status = [string]$gpu.Status
            currentHorizontalResolution = $gpu.CurrentHorizontalResolution
            currentVerticalResolution = $gpu.CurrentVerticalResolution
        }
    }
} catch { }

$endpoint = Test-Endpoint $ServerBaseUrl
$supportedFamily = if ($isServer) { $version -ge (New-Object Version "6.2") } else { $version.Major -ge 10 }
$architectureMessage = if ($is64Bit) { "x64 operating system" } else { "x86 is unsupported" }
$displayMessage = if ($screens.Count -gt 0) { $screens.Count.ToString() + " display(s)" } else { $displayError }
$endpointMessage = if ($endpoint.reachable) { "HTTP " + $endpoint.statusCode } else { $endpoint.error }
$elevationMessage = if ($elevated) { "Administrator token" } else { "UAC elevation will be required" }
Add-Check "os-family" "P0" $supportedFamily ("Windows version " + $version)
Add-Check "x64-os" "P0" $is64Bit $architectureMessage
Add-Check "desktop-experience" "P0" (-not $isCore) ([string]$installationType)
Add-Check "native-runtime" "P0" $true ("Native Win32 client; installed .NET release " + $frameworkRelease + " is informational only")
Add-Check "interactive-session" "P0" ($interactive -and $sessionId -ne 0) ("Session " + $sessionId + ", " + $env:SESSIONNAME)
Add-Check "display" "P0" ($screens.Count -gt 0) $displayMessage
Add-Check "server-health" "P0" ([bool]$endpoint.reachable) $endpointMessage
Add-Check "elevated" "P1" $elevated $elevationMessage

$p0Failed = @($checks | Where-Object { $_.severity -eq "P0" -and -not $_.passed }).Count -gt 0
$p1Failed = @($checks | Where-Object { $_.severity -eq "P1" -and -not $_.passed }).Count -gt 0
$status = if ($p0Failed) { "Fail" } elseif ($p1Failed) { "Warn" } else { "Pass" }

$portableLogPath = if ($clientEvidence.found) {
    Join-Path (Split-Path -Parent $clientEvidence.path) "RotaLink-Native.log"
} else {
    Join-Path $PSScriptRoot "RotaLink-Native.log"
}
$processEvidence = @()
try {
    foreach ($process in @(Get-Process -Name RotaLink -ErrorAction SilentlyContinue)) {
        $processStartTime = ""
        $processPath = ""
        try { $processStartTime = $process.StartTime.ToUniversalTime().ToString("o") } catch { }
        try { $processPath = [string]$process.Path } catch { }
        $processEvidence += [pscustomobject][ordered]@{
            id = $process.Id
            sessionId = $process.SessionId
            startTimeUtc = $processStartTime
            path = $processPath
        }
    }
} catch { }
$serviceEvidence = [ordered]@{ found=$false; status=""; startType=""; error="" }
try {
    $service = Get-WmiObject -Class Win32_Service -Filter "Name='RotaLinkNativeRuntime'" -ErrorAction Stop
    if ($null -ne $service) {
        $serviceEvidence.found = $true
        $serviceEvidence.status = [string]$service.State
        $serviceEvidence.startType = [string]$service.StartMode
    }
} catch { $serviceEvidence.error = $_.Exception.Message }

$report = [pscustomobject][ordered]@{
    schemaVersion = 2
    targetId = $TargetId
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    status = $status
    client = $clientEvidence
    machine = [ordered]@{
        computerName = $env:COMPUTERNAME
        manufacturer = [string]$computer.Manufacturer
        model = [string]$computer.Model
        domain = [string]$computer.Domain
    }
    os = [ordered]@{
        caption = $normalizedProductName
        registryProductName = $productName
        version = $version.ToString()
        major = $version.Major
        minor = $version.Minor
        build = $version.Build
        productType = $productType
        server = $isServer
        installationType = [string]$installationType
        architecture = $architectureMessage
        servicePack = $servicePack
    }
    runtime = [ordered]@{
        dotNetFrameworkRelease = $frameworkRelease
        powershell = $PSVersionTable.PSVersion.ToString()
        process64Bit = [Environment]::Is64BitProcess
        elevated = $elevated
        identity = $identityName
        sessionId = $sessionId
        sessionName = [string]$env:SESSIONNAME
        interactive = $interactive
    }
    display = [ordered]@{
        dpiX = $dpiX
        dpiY = $dpiY
        screens = $screens
        error = $displayError
    }
    graphics = $gpus
    serverEndpoint = $endpoint
    diagnostics = [ordered]@{
        processes = $processEvidence
        service = $serviceEvidence
        portableLog = Read-LogTail $portableLogPath
    }
    checks = $checks
}

$json = $report | ConvertTo-Json -Depth 8
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Get-Location) ("rotalink-compatibility-" + $TargetId + ".json")
}
$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, (New-Object Text.UTF8Encoding($false)))
Write-Host ("RotaLink compatibility result: " + $status)
Write-Host ("Report: " + [IO.Path]::GetFullPath($OutputPath))
if ($p0Failed) { exit 2 }
exit 0
