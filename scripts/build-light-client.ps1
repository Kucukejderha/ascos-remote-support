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

foreach ($runtimeProject in @($serviceProject, $helperProject)) {
    dotnet restore $runtimeProject --configfile $nugetConfig -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed: $runtimeProject" }
    dotnet build $runtimeProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Runtime build failed: $runtimeProject" }
}
dotnet restore $project --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'RotaLink restore failed.' }
dotnet build $project -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'RotaLink build failed.' }

$bin = Join-Path $root "client\RemoteSupport.SessionAgent\bin\$Configuration\net48"
$artifactDir = Join-Path $root 'artifacts'
$output = Join-Path $artifactDir 'RotaLink.exe'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item -LiteralPath (Join-Path $bin 'RotaLink.exe') -Destination $output -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
Write-Host "RotaLink created: $output"
Write-Host "Size: $((Get-Item $output).Length) bytes"
Write-Host "SHA-256: $hash"
