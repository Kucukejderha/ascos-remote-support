param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'client\RemoteSupport.SessionAgent\RemoteSupport.SessionAgent.csproj'
$serviceProject = Join-Path $root 'client\RemoteSupport.Service\RemoteSupport.Service.csproj'
$helperProject = Join-Path $root 'client\RotaLink.SessionHelper\RotaLink.SessionHelper.csproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$buildAppData = Join-Path $root 'work\build-appdata'
New-Item -ItemType Directory -Force -Path (Join-Path $buildAppData 'NuGet') | Out-Null
$env:APPDATA = $buildAppData
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$repositoryPackages = Join-Path $root '.nuget-packages'
if (Test-Path -LiteralPath $repositoryPackages) { $packages = $repositoryPackages }
$env:NUGET_PACKAGES = $packages

foreach ($runtimeProject in @($serviceProject, $helperProject)) {
    dotnet restore $runtimeProject --configfile $nugetConfig -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed: $runtimeProject" }
    dotnet build $runtimeProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Runtime build failed: $runtimeProject" }
}
dotnet restore $project --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'RotaLink restore failed.' }
dotnet build $project -c $Configuration --no-restore -p:UnsignedDevelopment=true
if ($LASTEXITCODE -ne 0) { throw 'RotaLink build failed.' }

$bin = Join-Path $root "client\RemoteSupport.SessionAgent\bin\$Configuration\net48"
$artifactDir = Join-Path $root 'artifacts'
$output = Join-Path $artifactDir 'RotaLink-v1.1.0-alpha.22-UNSIGNED-DEVELOPMENT.exe'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item -LiteralPath (Join-Path $bin 'RotaLink.exe') -Destination $output -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
Write-Host "RotaLink created: $output"
Write-Host "Size: $((Get-Item $output).Length) bytes"
Write-Host "SHA-256: $hash"
Write-Warning 'This artifact enables the elevated interactive-token control runtime for controlled development tests only.'
Write-Warning 'It is unsigned and must not be published to customers. Use build-signed-client.ps1 for distribution.'
