# Generates the original artwork of the bundled "aqua" theme (wallpapers and logo) into themes/aqua/assets.
# Everything is drawn here from shapes and gradients, so the theme carries no third-party art.
# Usage (from the repository root): pwsh -File tools/aqua-art/make-art.ps1
param([string]$Out = (Join-Path $PSScriptRoot '../../themes/aqua/assets'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

function New-Color([string]$hex, [int]$alpha = 255) {
    $v = [Convert]::ToInt32($hex.TrimStart('#'), 16)
    [System.Drawing.Color]::FromArgb($alpha, ($v -shr 16) -band 255, ($v -shr 8) -band 255, $v -band 255)
}

# A soft glow: an ellipse whose color fades to transparent at its edge.
function Add-Glow($g, [float]$cx, [float]$cy, [float]$rx, [float]$ry, [string]$hex, [int]$alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $rx, $cy - $ry, 2 * $rx, 2 * $ry)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $brush.CenterColor = New-Color $hex $alpha
    $brush.SurroundColors = @((New-Color $hex 0))
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# A smooth wave band across the picture, filled with a vertical gradient.
function Add-Wave($g, [int]$w, [int]$h, [float]$baseY, [float]$amp, [float]$phase, [string]$top, [string]$bottom, [int]$alpha) {
    $points = for ($x = 0; $x -le $w; $x += 16) {
        $y = $baseY + $amp * [Math]::Sin(($x / $w) * 2 * [Math]::PI * 1.1 + $phase) + 0.35 * $amp * [Math]::Sin(($x / $w) * 2 * [Math]::PI * 2.7 + 2 * $phase)
        New-Object System.Drawing.PointF ([float]$x), ([float]$y)
    }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddCurve([System.Drawing.PointF[]]$points)
    $path.AddLine([float]$w, [float]$baseY, [float]$w, [float]$h)
    $path.AddLine([float]$w, [float]$h, 0, [float]$h)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, ($baseY - $amp * 1.5), $w, ($h - $baseY + $amp * 1.5)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (New-Color $top $alpha), (New-Color $bottom $alpha), 90
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# A silky ribbon: a wide band along a gentle curve, tilted, whose color fades in and out across its width.
function Add-Ribbon($g, [int]$w, [int]$h, [float]$y, [float]$thick, [float]$amp, [float]$phase, [float]$angle, [string]$hex, [int]$alpha) {
    $state = $g.Save()
    $g.TranslateTransform($w / 2, $h / 2); $g.RotateTransform($angle); $g.TranslateTransform(-$w, -$h / 2)
    $top = for ($x = 0; $x -le 2 * $w; $x += 24) { New-Object System.Drawing.PointF ([float]$x), ([float]($y + $amp * [Math]::Sin($x / $w * 2.2 + $phase))) }
    $bottom = for ($x = 2 * $w; $x -ge 0; $x -= 24) { New-Object System.Drawing.PointF ([float]$x), ([float]($y + $thick + 0.6 * $amp * [Math]::Sin($x / $w * 2.2 + $phase + 0.9))) }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddCurve([System.Drawing.PointF[]]$top); $path.AddCurve([System.Drawing.PointF[]]$bottom); $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, ($y - $amp), (2 * $w), ($thick + 2 * $amp)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (New-Color $hex 0), (New-Color $hex 0), 90
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = @((New-Color $hex 0), (New-Color $hex $alpha), (New-Color $hex ([int]($alpha * 0.35))), (New-Color $hex 0))
    $blend.Positions = @(0, 0.35, 0.7, 1)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose(); $g.Restore($state)
}

function New-Wallpaper([string]$file, [hashtable]$p) {
    $w = 2560; $h = 1440
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.CompositingQuality = 'HighQuality'
    $base = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, $w, $h), (New-Color $p.Top), (New-Color $p.Bottom), 70
    $g.FillRectangle($base, 0, 0, $w, $h); $base.Dispose()
    Add-Glow $g ($w * 0.22) ($h * 0.18) ($w * 0.45) ($h * 0.55) $p.Glow 110
    Add-Glow $g ($w * 0.85) ($h * 0.9) ($w * 0.4) ($h * 0.45) $p.Glow2 90
    Add-Ribbon $g $w $h ($h * 0.30) ($h * 0.20) ($h * 0.06) 0.3 -14 $p.Silk 70
    Add-Ribbon $g $w $h ($h * 0.52) ($h * 0.16) ($h * 0.05) 1.9 -14 $p.Silk 95
    Add-Ribbon $g $w $h ($h * 0.70) ($h * 0.22) ($h * 0.07) 3.4 -14 $p.Silk 60
    Add-Ribbon $g $w $h ($h * 0.42) ($h * 0.08) ($h * 0.03) 5.0 -14 $p.Shine 80
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
    $params = New-Object System.Drawing.Imaging.EncoderParameters 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 92L
    $bmp.Save($file, $codec, $params)
    $g.Dispose(); $bmp.Dispose()
}

# Dark: slate blue-grey with silky folds (the look the author picked as reference). Light: the same folds in ice.
New-Wallpaper (Join-Path $Out 'wallpaper-dark.jpg') @{
    Top = '#263142'; Bottom = '#10151D'; Glow = '#46597A'; Glow2 = '#2C3A52'; Silk = '#8FA6C4'; Shine = '#D6E2F2'
}
New-Wallpaper (Join-Path $Out 'wallpaper-light.jpg') @{
    Top = '#E4EAF1'; Bottom = '#AFBCCB'; Glow = '#FFFFFF'; Glow2 = '#C9D5E2'; Silk = '#FFFFFF'; Shine = '#FFFFFF'
}

# Logo: a rounded "V" made of two strokes, in the bar's text color (one file per variant).
function New-Logo([string]$file, [string]$hex) {
    $s = 128
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $pen = New-Object System.Drawing.Pen (New-Color $hex), 20
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 26, 30), (New-Object System.Drawing.PointF 64, 100), (New-Object System.Drawing.PointF 102, 30)))
    $dot = New-Object System.Drawing.SolidBrush (New-Color $hex)
    $g.FillEllipse($dot, 54, 6, 20, 20)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $dot.Dispose(); $pen.Dispose(); $g.Dispose(); $bmp.Dispose()
}

New-Logo (Join-Path $Out 'logo-light.png') '#1D1D1F'
New-Logo (Join-Path $Out 'logo-dark.png') '#F5F5F7'
Get-ChildItem $Out | ForEach-Object { '{0,-22} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }
