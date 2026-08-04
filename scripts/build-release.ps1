param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$hostOutput = Join-Path $artifacts "windows-host"
$taskAppData = Join-Path $root "work\dotnet-appdata"
$taskPackages = Join-Path $root "work\nuget-packages"

New-Item -ItemType Directory -Force -Path (Join-Path $taskAppData "NuGet"), $taskPackages | Out-Null
$env:APPDATA = $taskAppData
$env:NUGET_PACKAGES = $taskPackages

if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $hostOutput | Out-Null
dotnet publish (Join-Path $root "client\RemoteSupport.SessionAgent\RemoteSupport.SessionAgent.csproj") -c $Configuration -o $hostOutput --self-contained false --no-restore
if ($LASTEXITCODE -ne 0) { throw "Host publish failed." }

Copy-Item (Join-Path $PSScriptRoot "Install-ASCOS-RemoteSupport.ps1") $hostOutput
Copy-Item (Join-Path $PSScriptRoot "Uninstall-ASCOS-RemoteSupport.ps1") $hostOutput
$payload = Join-Path $artifacts "host-payload.zip"
Compress-Archive -Path @(
    (Join-Path $hostOutput 'RotaLink.exe')
) -DestinationPath $payload -CompressionLevel Optimal

$installerProject = Join-Path $root 'installer\RemoteSupport.Installer\RemoteSupport.Installer.csproj'
$offlineNuget = Join-Path $root 'work\offline-nuget'
if (!(Test-Path -LiteralPath $offlineNuget)) { throw "Offline Windows runtime packages are missing." }
dotnet restore $installerProject --configfile (Join-Path $root 'NuGet.Config') --source $offlineNuget
if ($LASTEXITCODE -ne 0) { throw "Installer restore failed." }
$installerOutput = Join-Path $artifacts 'installer-publish'
dotnet publish $installerProject -c $Configuration -o $installerOutput -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None --no-restore
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed." }
$installerFile = Join-Path $artifacts 'RotaLink-Kurulum.exe'
Copy-Item (Join-Path $installerOutput 'RotaLink-Kurulum.exe') $installerFile -Force

$portableFile = Join-Path $artifacts 'RotaLink.exe'
Copy-Item (Join-Path $hostOutput 'RotaLink.exe') $portableFile -Force

$archive = Join-Path $artifacts "Rotaniz-Remote-Support-Windows.zip"
Compress-Archive -Path (Join-Path $hostOutput "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerFile).Hash.ToLowerInvariant()
$portableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $portableFile).Hash.ToLowerInvariant()
@{ file = (Split-Path -Leaf $archive); sha256 = $hash; installerFile = (Split-Path -Leaf $installerFile); installerSha256 = $installerHash; portableFile = (Split-Path -Leaf $portableFile); portableSha256 = $portableHash; createdAt = [DateTimeOffset]::UtcNow.ToString('O') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'release-manifest.json') -Encoding UTF8
Write-Host "Release created: $archive"
