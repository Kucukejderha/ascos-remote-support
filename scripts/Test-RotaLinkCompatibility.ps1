param(
    [Parameter(Mandatory = $true)][string]$TargetId,
    [string]$OutputPath = "",
    [string]$ServerBaseUrl = "https://45.87.173.201.nip.io"
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

$report = [pscustomobject][ordered]@{
    schemaVersion = 1
    targetId = $TargetId
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    status = $status
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
