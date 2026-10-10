# Generates src/FullscreenStart/Assets/app.ico (2x2 tile glyph) as a multi-size PNG-based ICO.
param([string]$OutFile = "$PSScriptRoot\..\src\FullscreenStart\Assets\app.ico")

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [Math]::Max(1, [Math]::Round($s * 0.08))
    $gap = [Math]::Max(1, [Math]::Round($s * 0.07))
    $cell = ($s - 2 * $pad - $gap) / 2
    $colors = '#3AA0F3', '#0078D4', '#0078D4', '#005A9E'
    $i = 0
    foreach ($row in 0, 1) {
        foreach ($col in 0, 1) {
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($colors[$i]))
            $x = $pad + $col * ($cell + $gap)
            $y = $pad + $row * ($cell + $gap)
            $g.FillRectangle($brush, [single]$x, [single]$y, [single]$cell, [single]$cell)
            $brush.Dispose()
            $i++
        }
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutFile)).Path + '\' + (Split-Path $OutFile -Leaf), $out.ToArray())
Write-Host "Wrote $OutFile"
