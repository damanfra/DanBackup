# Gera src/DanBackup.App/Assets/icon.ico: seta descendo para dentro de uma bandeja, sobre fundo azul arredondado.
# Uso: powershell -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\DanBackup.App\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Fundo: quadrado arredondado com gradiente azul
    $r = 56 * $s
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.RectangleF (8 * $s), (8 * $s), (240 * $s), (240 * $s)
    $bg.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
    $bg.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
    $bg.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $bg.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $bg.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 56, 142, 245)), ([System.Drawing.Color]::FromArgb(255, 20, 70, 180)), 90
    $g.FillPath($grad, $bg)

    $white = [System.Drawing.Brushes]::White

    # Seta para baixo
    $arrow = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (108 * $s), (46 * $s)),
        (New-Object System.Drawing.PointF (148 * $s), (46 * $s)),
        (New-Object System.Drawing.PointF (148 * $s), (110 * $s)),
        (New-Object System.Drawing.PointF (184 * $s), (110 * $s)),
        (New-Object System.Drawing.PointF (128 * $s), (166 * $s)),
        (New-Object System.Drawing.PointF (72 * $s), (110 * $s)),
        (New-Object System.Drawing.PointF (108 * $s), (110 * $s)))
    $g.FillPolygon($white, $arrow)

    # Bandeja (U)
    $tray = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (20 * $s)
    $tray.LineJoin = 'Round'; $tray.StartCap = 'Round'; $tray.EndCap = 'Round'
    $g.DrawLines($tray, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (62 * $s), (150 * $s)),
        (New-Object System.Drawing.PointF (62 * $s), (200 * $s)),
        (New-Object System.Drawing.PointF (194 * $s), (200 * $s)),
        (New-Object System.Drawing.PointF (194 * $s), (150 * $s))))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($n in $sizes) { , (New-IconPng $n) }

# ICO com imagens PNG
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $bytes = $images[$i]
    $dim = if ($n -ge 256) { 0 } else { $n }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $images) { $w.Write($bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $out "icon.ico"), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $out "icon.png"), $images[6])
Write-Host "Ícone gerado em $out"
