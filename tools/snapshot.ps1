# Builds the app and renders its views to PNG files (no window is shown).
# Usage: tools\snapshot.ps1 [-Out <dir>] [-Query <search text>]
param(
    [string]$Out = "$env:TEMP\fs-snap",
    [string]$Query = "co"
)

$root = Split-Path $PSScriptRoot
dotnet build "$root\src\FullscreenStart" -v q -nologo | Select-String " error "
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

Remove-Item -LiteralPath $Out -Recurse -Force -ErrorAction SilentlyContinue
$exe = "$root\src\FullscreenStart\bin\Debug\net8.0-windows\FullscreenStart.exe"
$p = Start-Process $exe -ArgumentList "--snapshot", "`"$Out`"", "`"$Query`"" -PassThru
if (-not $p.WaitForExit(45000)) { $p | Stop-Process -Force; Write-Error "Timeout"; exit 1 }
if (Test-Path "$Out\error.txt") { Get-Content "$Out\error.txt"; exit 1 }
Get-ChildItem $Out | Select-Object Name, Length
