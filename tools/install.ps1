# Installs (or updates) Fullscreen Start for the current user:
#   - publishes a Release build to %LocalAppData%\Programs\FullscreenStart
#   - replaces a running instance
#   - creates the Start menu shortcut "Fullscreen Start"
#   - turns on "start with Windows" (skip with -NoAutostart)
# Layout and settings in %AppData%\FullscreenStart are kept.
param([switch]$NoAutostart)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\FullscreenStart'
$staging = Join-Path $env:TEMP ('FullscreenStart.publish.' + [guid]::NewGuid())

Write-Host 'Building...'
dotnet publish "$root\src\FullscreenStart" -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -o $staging -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$running = Get-Process FullscreenStart -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Stopping the running instance...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Installing to $dest"
# Windows (or a virus scan) can hold the old files for a moment after the process exits: retry.
for ($attempt = 1; $attempt -le 20; $attempt++) {
    try {
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force -ErrorAction Stop }
        New-Item -ItemType Directory -Path $dest | Out-Null
        Copy-Item "$staging\*" $dest -Recurse -ErrorAction Stop
        break
    } catch {
        if ($attempt -eq 20) { throw }
        Start-Sleep -Milliseconds 500
    }
}
Remove-Item $staging -Recurse -Force
$exe = Join-Path $dest 'FullscreenStart.exe'

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Fullscreen Start.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $dest
$link.Description = 'Vollbild-Startmenue'
$link.Save()

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoAutostart) {
    Remove-ItemProperty -Path $run -Name FullscreenStart -ErrorAction SilentlyContinue
} else {
    Set-ItemProperty -Path $run -Name FullscreenStart -Value "`"$exe`" --autostart"
    # Windows 11 skips Run entries without an "enabled" mark here (this also replaces a Task Manager "disabled").
    $approved = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
    if (-not (Test-Path $approved)) { New-Item -Path $approved -Force | Out-Null }
    Set-ItemProperty -Path $approved -Name FullscreenStart -Type Binary -Value ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0))
}

Start-Process $exe
Write-Host "Done. Autostart: $(-not $NoAutostart)"
