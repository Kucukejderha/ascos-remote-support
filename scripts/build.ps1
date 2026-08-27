param(
    [string]$Configuration = 'Release',
    [switch]$Full,
    [string]$SignThumbprint = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$nugetConfig = Join-Path $root 'NuGet.Config'
$buildAppData = Join-Path $root 'work\build-appdata'
New-Item -ItemType Directory -Force -Path (Join-Path $buildAppData 'NuGet') | Out-Null
$env:APPDATA = $buildAppData

$nativeBuild = Join-Path $root 'native\build'
$nativeOutput = Join-Path $nativeBuild 'x64\RotaLink.NativeCapture.exe'

function Get-ShortPath([string]$path) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $fso = New-Object -ComObject Scripting.FileSystemObject
    if (Test-Path -LiteralPath $resolved -PathType Container) { return $fso.GetFolder($resolved).ShortPath }
    return $fso.GetFile($resolved).ShortPath
}

function Build-NativeCapture {
    $vcvarsCandidates = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat'
    )
    $vcvars = $vcvarsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $vcvars) {
        Write-Warning 'MSVC vcvars64.bat was not found; native capture is skipped (GDI fallback remains active).'
        return
    }
    $sdkRoot = 'C:\Program Files (x86)\Windows Kits\10'
    $sdkVersion = Get-ChildItem (Join-Path $sdkRoot 'Include') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
    if (-not $sdkVersion) {
        Write-Warning 'Windows SDK 10 was not found; native capture is skipped (GDI fallback remains active).'
        return
    }
    $vsBase = Split-Path (Split-Path (Split-Path (Split-Path $vcvars -Parent) -Parent) -Parent) -Parent
    $msvcRoot = Get-ChildItem (Join-Path $vsBase 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $msvcRoot) {
        Write-Warning 'MSVC toolset was not found; native capture is skipped (GDI fallback remains active).'
        return
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $nativeBuild 'x64') | Out-Null
    $cl8 = Get-ShortPath (Join-Path $msvcRoot 'bin\Hostx64\x64\cl.exe')
    $msvc8 = Get-ShortPath $msvcRoot
    $sdk8 = Get-ShortPath $sdkRoot
    $src8 = Get-ShortPath (Join-Path $root 'native\RotaLink.NativeHost')
    $out8 = Get-ShortPath (Join-Path $nativeBuild 'x64')

    $bat = Join-Path $env:TEMP ('build-native-' + [Guid]::NewGuid().ToString('N') + '.bat')
    $content = "@echo off`n" +
        "set INCLUDE=$msvc8\include;$sdk8\Include\$sdkVersion\ucrt;$sdk8\Include\$sdkVersion\um;$sdk8\Include\$sdkVersion\shared`n" +
        "set LIB=$msvc8\lib\x64;$sdk8\Lib\$sdkVersion\ucrt\x64;$sdk8\Lib\$sdkVersion\um\x64`n" +
        "`"$cl8`" /nologo /W4 /WX /permissive- /EHsc /O2 /std:c++20 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX " +
        "/Fo$out8\ /Fe$out8\RotaLink.NativeCapture.exe " +
        "$src8\DesktopDuplicator.cpp $src8\GpuColorConverter.cpp $src8\H264Encoder.cpp $src8\SharedFrameBuffer.cpp $src8\main.cpp " +
        "/link d3d11.lib dxgi.lib ole32.lib oleaut32.lib mf.lib mfplat.lib mfuuid.lib user32.lib"
    Set-Content -Path $bat -Value $content -Encoding ASCII
    try {
        cmd /c $bat 2>&1 | Out-Null
        if (-not (Test-Path -LiteralPath $nativeOutput)) {
            Write-Warning 'Native capture build failed; GDI fallback remains active.'
        } else {
            Write-Host "Native capture built: $nativeOutput"
        }
    } finally {
        Remove-Item -LiteralPath $bat -Force -ErrorAction SilentlyContinue
    }
}

Build-NativeCapture

$serviceProject = Join-Path $root 'client\RemoteSupport.Service\RemoteSupport.Service.csproj'
$helperProject = Join-Path $root 'client\RotaLink.SessionHelper\RotaLink.SessionHelper.csproj'
$agentProject = Join-Path $root 'client\RemoteSupport.SessionAgent\RemoteSupport.SessionAgent.csproj'

foreach ($runtimeProject in @($serviceProject, $helperProject)) {
    dotnet restore $runtimeProject --configfile $nugetConfig -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed: $runtimeProject" }
    dotnet build $runtimeProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Runtime build failed: $runtimeProject" }
}
dotnet restore $agentProject --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'RotaLink restore failed.' }
dotnet build $agentProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'RotaLink build failed.' }

$bin = Join-Path $root "client\RemoteSupport.SessionAgent\bin\$Configuration\net48"
$output = Join-Path $artifacts 'RotaLink.exe'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Copy-Item -LiteralPath (Join-Path $bin 'RotaLink.exe') -Destination $output -Force
$portableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
Write-Host "RotaLink created: $output"
Write-Host "Size: $((Get-Item $output).Length) bytes"
Write-Host "SHA-256: $portableHash"

if ($SignThumbprint) {
    Sign-File $output $SignThumbprint
}

if (-not $Full) { return }

$hostOutput = Join-Path $artifacts 'windows-host'
New-Item -ItemType Directory -Force -Path $hostOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $bin 'RotaLink.exe') -Destination $hostOutput
Copy-Item (Join-Path $PSScriptRoot 'Install-ASCOS-RemoteSupport.ps1') $hostOutput
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-ASCOS-RemoteSupport.ps1') $hostOutput
$payload = Join-Path $artifacts 'host-payload.zip'
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
Compress-Archive -Path @((Join-Path $hostOutput 'RotaLink.exe')) -DestinationPath $payload -CompressionLevel Optimal

$installerProject = Join-Path $root 'installer\RemoteSupport.Installer\RemoteSupport.Installer.csproj'
dotnet restore $installerProject --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Installer restore failed.' }
$installerOutput = Join-Path $artifacts 'installer-publish'
dotnet publish $installerProject -c $Configuration -o $installerOutput -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
$installerFile = Join-Path $artifacts 'RotaLink-Kurulum.exe'
Copy-Item (Join-Path $installerOutput 'RotaLink-Kurulum.exe') $installerFile -Force
if ($SignThumbprint) {
    Sign-File $installerFile $SignThumbprint
}

$archive = Join-Path $artifacts 'Rotaniz-Remote-Support-Windows.zip'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $hostOutput '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerFile).Hash.ToLowerInvariant()
@{ file = (Split-Path -Leaf $archive); sha256 = $hash; installerFile = (Split-Path -Leaf $installerFile); installerSha256 = $installerHash; portableFile = (Split-Path -Leaf $output); portableSha256 = $portableHash; createdAt = [DateTimeOffset]::UtcNow.ToString('O') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'release-manifest.json') -Encoding UTF8
Write-Host "Release created: $archive"

function Sign-File([string]$filePath, [string]$thumbprint) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) {
        Write-Warning "signtool.exe not found; $filePath was not signed."
        return
    }
    & $signtool.FullName sign /sha1 $thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $filePath
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $filePath." }
    Write-Host "Signed: $filePath"
}
