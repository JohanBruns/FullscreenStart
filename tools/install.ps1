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

# Autostart of earlier versions (Run key). From a terminal inside a packaged (MSIX) app that write went to
# the package's private registry and never took effect; the task below is registered for real.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name FullscreenStart -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' `
    -Name FullscreenStart -ErrorAction SilentlyContinue

if ($NoAutostart) {
    Unregister-ScheduledTask -TaskName FullscreenStart -Confirm:$false -ErrorAction SilentlyContinue
} else {
    # Same task as the tray menu's "Mit Windows starten": at this user's sign-in, normal rights and priority.
    $user = "$env:USERDOMAIN\$env:USERNAME"
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $trigger.Delay = 'PT3S'
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Priority 4
    Register-ScheduledTask -TaskName FullscreenStart -Force `
        -Description 'Startet Fullscreen Start bei der Anmeldung.' `
        -Action (New-ScheduledTaskAction -Execute $exe -Argument '--autostart' -WorkingDirectory $dest) `
        -Trigger $trigger -Settings $settings `
        -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited) | Out-Null
}

if ($NoAutostart) {
    Start-Process $exe
} else {
    # Started by the Task Scheduler, the menu runs outside any app container this script may run in, so it
    # uses the real %AppData% (layout, settings) just like after the next sign-in.
    Start-ScheduledTask -TaskName FullscreenStart
}
Write-Host "Done. Autostart: $(-not $NoAutostart)"
