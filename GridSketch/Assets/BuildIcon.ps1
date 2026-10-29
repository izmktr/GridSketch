# Generate Windows ICO sizes from the same vector geometry as GridSketch.svg.
Add-Type -AssemblyName System.Drawing
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$iconImages = @()
foreach ($iconSize in $iconSizes) {
    $bitmap = New-Object System.Drawing.Bitmap ($iconSize * 4), ($iconSize * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($iconSize * 4 / 256.0), ($iconSize * 4 / 256.0))
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1457A6'))
    $yellow = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFD54F'))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(8, 8, 96, 96, 180, 90)
    $path.AddArc(152, 8, 96, 96, 270, 90)
    $path.AddArc(152, 152, 96, 96, 0, 90)
    $path.AddArc(8, 152, 96, 96, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($blue, $path)
    $paper = New-Object System.Drawing.Drawing2D.GraphicsPath
    $paper.AddArc(40, 40, 16, 16, 180, 90)
    $paper.AddArc(200, 40, 16, 16, 270, 90)
    $paper.AddArc(200, 200, 16, 16, 0, 90)
    $paper.AddArc(40, 200, 16, 16, 90, 90)
    $paper.CloseFigure()
    $graphics.FillPath([System.Drawing.Brushes]::White, $paper)
    $graphics.FillRectangle($yellow, 104, 104, 48, 48)
    $graphics.FillRectangle($yellow, 160, 104, 48, 48)
    $pen = New-Object System.Drawing.Pen $blue, 8
    $graphics.DrawLine($pen, 100, 40, 100, 216)
    $graphics.DrawLine($pen, 156, 40, 156, 216)
    $graphics.DrawLine($pen, 40, 100, 216, 100)
    $graphics.DrawLine($pen, 40, 156, 216, 156)
    $small = New-Object System.Drawing.Bitmap $iconSize, $iconSize
    $outputGraphics = [System.Drawing.Graphics]::FromImage($small)
    $outputGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $outputGraphics.DrawImage($bitmap, 0, 0, $iconSize, $iconSize)
    $stream = New-Object System.IO.MemoryStream
    $small.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $iconImages += ,$stream.ToArray()
    if ($iconSize -eq 256) { $small.Save((Join-Path $PSScriptRoot 'GridSketch.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $outputGraphics.Dispose(); $small.Dispose(); $pen.Dispose()
    $paper.Dispose(); $path.Dispose(); $yellow.Dispose(); $blue.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'GridSketch.ico'))
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    for ($i = 0; $i -lt $iconSizes.Count; $i++) {
        $sizeByte = if ($iconSizes[$i] -eq 256) { 0 } else { $iconSizes[$i] }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$iconImages[$i].Length); $writer.Write([uint32]$offset)
        $offset += $iconImages[$i].Length
    }
    foreach ($bytes in $iconImages) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
