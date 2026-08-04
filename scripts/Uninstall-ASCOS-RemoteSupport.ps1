param([string]$InstallDirectory = "$env:LOCALAPPDATA\ASCOS\RemoteSupport")
$ErrorActionPreference = "Stop"
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ASCOS Uzaktan Destek.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
Write-Host "Shortcut removed. Close this window, then remove: $InstallDirectory"
