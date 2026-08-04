param([string]$InstallDirectory = "$env:LOCALAPPDATA\Rotaniz\RotaLink")
$ErrorActionPreference = "Stop"
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RotaLink.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
Write-Host "Shortcut removed. Close this window, then remove: $InstallDirectory"
