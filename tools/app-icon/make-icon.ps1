# Draws Veneer's application icon (the Aqua logo's "V" on a rounded slate square) into src/Veneer.App/veneer.ico.
# Original artwork, MIT like the code. Usage (from the repository root): pwsh -File tools/app-icon/make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '../../src/Veneer.App/veneer.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconImage([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    # Rounded square, slate with a faint top highlight.
    $r = [float]($s * 0.22); $d = 2 * $r; $w = [float]($s - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, $s)),
        ([System.Drawing.Color]::FromArgb(255, 0x3A, 0x45, 0x58)), ([System.Drawing.Color]::FromArgb(255, 0x1B, 0x22, 0x2E))
    $g.FillPath($fill, $path)
    # The logo's "V" and dot, scaled from its 128 px drawing into the middle of the square.
    $k = $s / 128.0 * 0.62; $o = $s * 0.19
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0xF5, 0xF5, 0xF7)), ([float][Math]::Max(1.6, 20 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($o + 26 * $k, $o + 34 * $k), [System.Drawing.PointF]::new($o + 64 * $k, $o + 104 * $k), [System.Drawing.PointF]::new($o + 102 * $k, $o + 34 * $k)))
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF5, 0xF5, 0xF7))
    $g.FillEllipse($dot, [float]($o + 54 * $k), [float]($o + 8 * $k), [float](20 * $k), [float](20 * $k))
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $dot.Dispose(); $pen.Dispose(); $fill.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    return , $ms.ToArray()
}

# An .ico file with PNG-compressed images (supported since Windows Vista).
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = $sizes | ForEach-Object { , (New-IconImage $_) }
$file = New-Object System.IO.MemoryStream
$wr = New-Object System.IO.BinaryWriter $file
$wr.Write([uint16]0); $wr.Write([uint16]1); $wr.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $wr.Write([byte]($sz % 256)); $wr.Write([byte]($sz % 256)); $wr.Write([byte]0); $wr.Write([byte]0)
    $wr.Write([uint16]1); $wr.Write([uint16]32); $wr.Write([uint32]$len); $wr.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $wr.Write($img) }
$wr.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Out) | Join-Path -ChildPath (Split-Path $Out -Leaf)), $file.ToArray())
'{0} {1:N0} KB' -f (Split-Path $Out -Leaf), ($file.Length / 1KB)
