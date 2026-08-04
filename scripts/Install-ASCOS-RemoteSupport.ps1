param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https://')][string]$ServerUrl,
    [string]$InstallDirectory = "$env:LOCALAPPDATA\Rotaniz\RotaLink",
    [string]$ShortcutDirectory = ([Environment]::GetFolderPath('Desktop'))
)
$ErrorActionPreference = "Stop"
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -notlike 'Install-*' -and $_.Name -notlike 'Uninstall-*' } | Copy-Item -Destination $InstallDirectory -Force
Copy-Item (Join-Path $source 'Uninstall-ASCOS-RemoteSupport.ps1') $InstallDirectory -Force

$exe = Join-Path $InstallDirectory 'RotaLink.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Host executable was not found in the package." }
$shell = New-Object -ComObject WScript.Shell
New-Item -ItemType Directory -Path $ShortcutDirectory -Force | Out-Null
$shortcutPath = Join-Path $ShortcutDirectory 'RotaLink.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.Arguments = '"' + $ServerUrl.TrimEnd('/') + '"'
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.Description = 'Rotaniz Remote Support'
$shortcut.Save()
Write-Host "Rotaniz Remote Support kuruldu. Kısayol: $shortcutPath"
