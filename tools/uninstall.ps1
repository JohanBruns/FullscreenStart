# Removes Fullscreen Start for the current user (program, Start menu shortcut, autostart).
# Layout and settings in %AppData%\FullscreenStart are kept unless -RemoveData is given.
param([switch]$RemoveData)

$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\FullscreenStart'

Get-Process FullscreenStart -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName FullscreenStart -Confirm:$false -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name FullscreenStart -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' `
    -Name FullscreenStart -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Fullscreen Start.lnk') -ErrorAction SilentlyContinue
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
if ($RemoveData) { Remove-Item (Join-Path $env:APPDATA 'FullscreenStart') -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Fullscreen Start removed.'
