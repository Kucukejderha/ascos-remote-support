param(
    [string]$Configuration = 'Release',
    [string]$PfxPath,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nugetConfig = Join-Path $root 'NuGet.Config'
$helperProject = Join-Path $root 'client\RotaLink.SessionHelper\RotaLink.SessionHelper.csproj'
$serviceProject = Join-Path $root 'client\RemoteSupport.Service\RemoteSupport.Service.csproj'
$clientProject = Join-Path $root 'client\RemoteSupport.SessionAgent\RemoteSupport.SessionAgent.csproj'
$helperExe = Join-Path $root "client\RotaLink.SessionHelper\bin\$Configuration\net48\RotaLink.SessionHelper.exe"
$serviceExe = Join-Path $root "client\RemoteSupport.Service\bin\$Configuration\net48\RotaLink.Service.exe"
$clientExe = Join-Path $root "client\RemoteSupport.SessionAgent\bin\$Configuration\net48\RotaLink.exe"

if ([string]::IsNullOrWhiteSpace($PfxPath) -eq [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Specify exactly one signing identity: -PfxPath or -CertificateThumbprint.'
}
if ($PfxPath -and !(Test-Path -LiteralPath $PfxPath)) { throw "PFX file not found: $PfxPath" }
if ($PfxPath -and [string]::IsNullOrEmpty($env:ROTALINK_SIGNING_PASSWORD)) {
    throw 'Set ROTALINK_SIGNING_PASSWORD in the build environment. The password is never accepted as a command-line argument.'
}

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (!$signTool) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -Match '\\x64\\signtool\.exe$' |
        Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
}
if (!$signTool) { throw 'signtool.exe was not found. Install the Windows SDK signing tools.' }

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}

function Invoke-Sign([string]$File) {
    $arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl)
    if ($PfxPath) {
        $arguments += @('/f', (Resolve-Path -LiteralPath $PfxPath).Path, '/p', $env:ROTALINK_SIGNING_PASSWORD)
    } else {
        $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '')
        $arguments += @('/sha1', $normalizedThumbprint)
    }
    $arguments += $File
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed: $File" }
    & $signTool verify /pa /all $File
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed: $File" }
}

$buildAppData = Join-Path $root 'work\signed-build-appdata'
New-Item -ItemType Directory -Force -Path (Join-Path $buildAppData 'NuGet') | Out-Null
$env:APPDATA = $buildAppData
$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'

foreach ($project in @($helperProject, $serviceProject, $clientProject)) {
    Invoke-DotNet -Arguments @('restore', $project, '--configfile', $nugetConfig, '-p:NuGetAudit=false')
}

Invoke-DotNet -Arguments @('build', $helperProject, '-c', $Configuration, '--no-restore', '-t:Rebuild')
Invoke-Sign $helperExe
Invoke-DotNet -Arguments @('build', $serviceProject, '-c', $Configuration, '--no-restore', '-t:Rebuild')
Invoke-Sign $serviceExe

# The client build must happen after both runtime executables are signed because
# it embeds their exact signed bytes into the one-file customer download.
Invoke-DotNet -Arguments @('build', $clientProject, '-c', $Configuration, '--no-restore', '-t:Rebuild')
Invoke-Sign $clientExe

$artifactDirectory = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$versionedArtifact = Join-Path $artifactDirectory 'RotaLink-v1.1.0-alpha.17.exe'
$stableArtifact = Join-Path $artifactDirectory 'RotaLink.exe'
Copy-Item -LiteralPath $clientExe -Destination $versionedArtifact -Force
Copy-Item -LiteralPath $clientExe -Destination $stableArtifact -Force

foreach ($artifact in @($versionedArtifact, $stableArtifact)) {
    & $signTool verify /pa /all $artifact
    if ($LASTEXITCODE -ne 0) { throw "Published artifact signature verification failed: $artifact" }
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $versionedArtifact).Hash.ToLowerInvariant()
Write-Host "Signed RotaLink created: $versionedArtifact"
Write-Host "Size: $((Get-Item $versionedArtifact).Length) bytes"
Write-Host "SHA-256: $hash"
