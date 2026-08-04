param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'client\RemoteSupport.SessionAgent\RemoteSupport.SessionAgent.csproj'
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

dotnet restore $project --source 'https://api.nuget.org/v3/index.json'
if ($LASTEXITCODE -ne 0) { throw 'RotaLink restore failed.' }
dotnet build $project -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'RotaLink build failed.' }

$bin = Join-Path $root "client\RemoteSupport.SessionAgent\bin\$Configuration\net48"
$artifactDir = Join-Path $root 'artifacts'
$output = Join-Path $artifactDir 'RotaLink.exe'
$ilRepack = Join-Path $packages 'ilrepack\2.0.29\tools\ILRepack.exe'
$framework = Join-Path $packages 'microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8'
if (!(Test-Path $ilRepack) -or !(Test-Path $framework)) { throw 'ILRepack or .NET Framework reference package is missing.' }
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$inputs = @((Join-Path $bin 'RotaLink.exe')) + @(Get-ChildItem $bin -Filter '*.dll' | ForEach-Object FullName)
& $ilRepack /target:winexe "/lib:$bin" "/targetplatform:v4,$framework" /internalize /ndebug "/out:$output" $inputs
if ($LASTEXITCODE -ne 0) { throw 'RotaLink single-file merge failed.' }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
Write-Host "RotaLink created: $output"
Write-Host "Size: $((Get-Item $output).Length) bytes"
Write-Host "SHA-256: $hash"
