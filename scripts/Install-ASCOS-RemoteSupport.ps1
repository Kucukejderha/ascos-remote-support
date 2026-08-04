param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https://')][string]$ServerUrl,
    [string]$InstallDirectory = "$env:LOCALAPPDATA\ASCOS\RemoteSupport",
    [string]$ShortcutDirectory = ([Environment]::GetFolderPath('Desktop'))
)
$ErrorActionPreference = "Stop"
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -notlike 'Install-*' -and $_.Name -notlike 'Uninstall-*' } | Copy-Item -Destination $InstallDirectory -Force
Copy-Item (Join-Path $source 'Uninstall-ASCOS-RemoteSupport.ps1') $InstallDirectory -Force

$exe = Join-Path $InstallDirectory 'ASCOS.RemoteSupport.Host.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Host executable was not found in the package." }
$shell = New-Object -ComObject WScript.Shell
New-Item -ItemType Directory -Path $ShortcutDirectory -Force | Out-Null
$shortcutPath = Join-Path $ShortcutDirectory 'ASCOS Uzaktan Destek.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.Arguments = '"' + $ServerUrl.TrimEnd('/') + '"'
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.Description = 'ASCOS Uzaktan Destek'
$shortcut.Save()
Write-Host "ASCOS Remote Support installed. Shortcut: $shortcutPath"
