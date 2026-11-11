# 生成 macOS 风格应用图标（squircle + 平滑渐变 + 白色电池字形 + 抖动去色带）
# 用法：powershell -File scripts\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $root 'src\BatteryWidget\app.ico'
$previewPath = Join-Path $root 'docs\icon-preview.png'

function New-SquirclePath {
    param([float]$Left, [float]$Top, [float]$Size, [float]$N = 5.0)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cx = $Left + $Size / 2.0
    $cy = $Top + $Size / 2.0
    $r = $Size / 2.0
    $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'

    $steps = 720
    for ($i = 0; $i -lt $steps; $i++) {
        $t = 2.0 * [Math]::PI * $i / $steps
        $ct = [Math]::Cos($t)
        $st = [Math]::Sin($t)
        $x = $r * [Math]::Sign($ct) * [Math]::Pow([Math]::Abs($ct), 2.0 / $N)
        $y = $r * [Math]::Sign($st) * [Math]::Pow([Math]::Abs($st), 2.0 / $N)
        $points.Add((New-Object System.Drawing.PointF(([float]($cx + $x)), ([float]($cy + $y)))))
    }

    $path.AddPolygon($points.ToArray())
    return $path
}

function New-RoundedRectPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($Radius * 2.0, [Math]::Min($Width, $Height))
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$canvas = 1024
$bitmap = New-Object System.Drawing.Bitmap($canvas, $canvas)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

$margin = 76.0
$size = [float]($canvas - $margin * 2.0)
$squircle = New-SquirclePath -Left $margin -Top $margin -Size $size -N 5.0

# 主体渐变：浅蓝到深蓝，两段插值减少色带
$rectTop = [float]$margin
$rectHeight = [float]$size
$rect = New-Object System.Drawing.RectangleF($rectTop, $rectTop, [float]$size, $rectHeight)
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 118, 214, 255),
    [System.Drawing.Color]::FromArgb(255, 10, 92, 220),
    90.0)
$gradient.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
$g.FillPath($gradient, $squircle)

# 顶部柔光（radial），做出 macOS 那种上亮下暗的体积感
$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowSize = [float]($size * 1.05)
$glowPath.AddEllipse([float]($margin + $size * 0.5 - $glowSize / 2.0),
                     [float]($margin + $size * 0.16 - $glowSize / 2.0),
                     $glowSize, $glowSize)
$glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
$glow.CenterColor = [System.Drawing.Color]::FromArgb(72, 255, 255, 255)
$glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
$glow.FocusScales = New-Object System.Drawing.PointF(0.15, 0.15)
$g.FillPath($glow, $squircle)

# 抖动噪点，消除渐变色带
$noise = New-Object System.Drawing.Bitmap(64, 64)
$random = New-Object System.Random(2026)
for ($ny = 0; $ny -lt 64; $ny++) {
    for ($nx = 0; $nx -lt 64; $nx++) {
        $alpha = $random.Next(0, 9)
        $noise.SetPixel($nx, $ny, [System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))
    }
}
$noiseBrush = New-Object System.Drawing.TextureBrush($noise)
$noiseBrush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::Tile
$g.FillPath($noiseBrush, $squircle)

# 内描边
$rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(80, 255, 255, 255), 5)
$g.DrawPath($rimPen, $squircle)

# ---------------- 电池字形 ----------------
$bodyWidth = [float]($size * 0.46)
$bodyHeight = [float]($size * 0.215)
$bodyX = [float]($margin + ($size - $bodyWidth) / 2.0 - $size * 0.032)
$bodyY = [float]($margin + ($size - $bodyHeight) / 2.0)
$stroke = [float]($size * 0.042)
$bodyRadius = [float]($size * 0.055)

$body = New-RoundedRectPath -X $bodyX -Y $bodyY -Width $bodyWidth -Height $bodyHeight -Radius $bodyRadius

# 字形阴影
$shadowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(34, 0, 20, 60), $stroke)
$shadowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$state = $g.Save()
$g.TranslateTransform(0, [float]($size * 0.012))
$g.DrawPath($shadowPen, $body)
$g.Restore($state)

$batteryPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
$batteryPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($batteryPen, $body)

# 右侧电极
$nubWidth = [float]($size * 0.034)
$nubHeight = [float]($size * 0.088)
$nubX = [float]($bodyX + $bodyWidth + $size * 0.006)
$nubY = [float]($bodyY + ($bodyHeight - $nubHeight) / 2.0)
$nub = New-RoundedRectPath -X $nubX -Y $nubY -Width $nubWidth -Height $nubHeight -Radius ([float]($nubWidth / 2.0))
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$g.FillPath($whiteBrush, $nub)

# 内部电量填充
$inset = [float]($stroke / 2.0 + $size * 0.012)
$innerX = [float]($bodyX + $inset)
$innerY = [float]($bodyY + $inset)
$innerFullWidth = [float]($bodyWidth - $inset * 2.0)
$innerHeight = [float]($bodyHeight - $inset * 2.0)
$innerWidth = [float]($innerFullWidth * 0.72)
$inner = New-RoundedRectPath -X $innerX -Y $innerY -Width $innerWidth -Height $innerHeight -Radius ([float]($innerHeight * 0.34))
$g.FillPath($whiteBrush, $inner)

$g.Dispose()

$bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

# ---------------- 多尺寸 ICO（BMP 帧，兼容性最好） ----------------
function New-IcoFrame {
    param([System.Drawing.Bitmap]$Source, [int]$Size)

    $frame = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $fg = [System.Drawing.Graphics]::FromImage($frame)
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.Clear([System.Drawing.Color]::Transparent)
    $fg.DrawImage($Source, (New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)))
    $fg.Dispose()

    # 256 用 PNG 帧（Windows 大图标标准），其余用 BMP 帧
    if ($Size -ge 256) {
        $pngStream = New-Object System.IO.MemoryStream
        $frame.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes = $pngStream.ToArray()
        $pngStream.Dispose()
        $frame.Dispose()
        return $pngBytes
    }

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $data = $frame.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $Size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $frame.UnlockBits($data)
    $frame.Dispose()

    $maskStride = [int]([Math]::Floor(($Size + 31) / 32) * 4)
    $mask = New-Object byte[] ($maskStride * $Size)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$Size)
    $bw.Write([Int32]($Size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($Size * $Size * 4 + $mask.Length))
    $bw.Write([Int32]0)
    $bw.Write([Int32]0)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]0)

    for ($y = $Size - 1; $y -ge 0; $y--) {
        $bw.Write($pixels, $y * $stride, $Size * 4)
    }
    $bw.Write($mask)
    $bw.Flush()

    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return $bytes
}

$sizes = @(256, 128, 64, 48, 32, 16)
$frames = @()
foreach ($s in $sizes) {
    $frames += , (New-IcoFrame -Source $bitmap -Size $s)
}
$bitmap.Dispose()

$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame, 0, $frame.Length) }
$writer.Flush()
$writer.Close()

Write-Host "已生成：$icoPath（$($sizes -join '/')）"
Write-Host "预览：$previewPath"