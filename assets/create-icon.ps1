$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 创建圆角矩形路径，供各尺寸图标共用。
function New-RoundedRectangle {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc(($X + $Width - $diameter), $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(($X + $Width - $diameter), ($Y + $Height - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($X, ($Y + $Height - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return ,$path
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
    # 透明画布，浅色线条配细深色外描边，在深浅任务栏上都可辨认。
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $page = New-RoundedRectangle 49 48 158 183 18
    $clip = New-RoundedRectangle 94 25 68 42 12
    $stroke = if ($size -eq 16) { 16 } else { 12 }
    $outline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(55, 65, 81), ($stroke + 8))
    $outline.StartCap = $outline.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(245, 245, 247), $stroke)
    $line.StartCap = $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawPath($outline, $page)
    $graphics.DrawPath($line, $page)
    # 剪夹处切断板顶线，保持透明留白和连续轮廓。
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.FillRectangle([System.Drawing.Brushes]::Transparent, 87, 35, 82, 31)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.DrawPath($outline, $clip)
    $graphics.DrawPath($line, $clip)
    foreach ($pen in @($outline, $line)) {
        $graphics.DrawLine($pen, 85, 110, 171, 110)
        $graphics.DrawLine($pen, 85, 149, 171, 149)
        $graphics.DrawLine($pen, 85, 188, 145, 188)
    }
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $PSScriptRoot 'clipboard.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $stream.Dispose()
    $line.Dispose()
    $outline.Dispose()
    $page.Dispose()
    $clip.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

# ICO 使用 PNG 帧，覆盖托盘、任务栏和资源管理器常用尺寸。
$iconStream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'clipboard.ico'))
$writer = New-Object System.IO.BinaryWriter($iconStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()
