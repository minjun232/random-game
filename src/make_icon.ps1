Add-Type -AssemblyName System.Drawing

$srcImgPath = Join-Path $PSScriptRoot "icon_source.jpg"
if (-not (Test-Path -LiteralPath $srcImgPath)) {
    throw "Missing $srcImgPath"
}

$rawImg = [System.Drawing.Image]::FromFile($srcImgPath)

# Center tightly on the Lucky Gacha Capsule + sparkles so it fills the squircle cleanly
$cropX = [int]($rawImg.Width * 0.135)
$cropY = [int]($rawImg.Height * 0.145)
$cropW = [int]($rawImg.Width * 0.73)
$cropH = [int]($rawImg.Height * 0.73)
$srcRect = New-Object System.Drawing.Rectangle($cropX, $cropY, $cropW, $cropH)

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2.0
    $gp.AddArc($x, $y, $d, $d, 180, 90)
    $gp.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $gp.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $gp.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $gp.CloseFigure()
    return $gp
}

# Build a 512x512 master squircle icon with smooth anti-aliased edges
$masterSz = 512
$masterBmp = New-Object System.Drawing.Bitmap($masterSz, $masterSz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$mg = [System.Drawing.Graphics]::FromImage($masterBmp)
$mg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$mg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$mg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$mg.Clear([System.Drawing.Color]::Transparent)

$pad = 10.0
$boxSz = $masterSz - 2.0 * $pad
$radius = 116.0
$sqPath = New-RoundedRectPath $pad $pad $boxSz $boxSz $radius

$scaledSrc = New-Object System.Drawing.Bitmap($masterSz, $masterSz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($scaledSrc)
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sg.DrawImage($rawImg, (New-Object System.Drawing.Rectangle(0, 0, $masterSz, $masterSz)), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$sg.Dispose()

$texBrush = New-Object System.Drawing.TextureBrush($scaledSrc)
$mg.FillPath($texBrush, $sqPath)

# Subtle clean Apple/Toss-style soft inner specular border (no heavy yellow ring)
$rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(75, 255, 255, 255), 4.0)
$mg.DrawPath($rimPen, $sqPath)

$texBrush.Dispose()
$scaledSrc.Dispose()
$sqPath.Dispose()
$mg.Dispose()
$rawImg.Dispose()

function Resize-IconFrame([System.Drawing.Bitmap]$src, [int]$sz) {
    $b = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $sz, $sz)))
    $g.Dispose()
    return $b
}

$sizes = @(256, 64, 48, 32, 24, 16)
$pngChunks = @()
$scratchDir = "C:\Users\sundo\.gemini\antigravity\brain\47430b06-96e2-400a-8609-da55a85c5b37\scratch"

foreach ($s in $sizes) {
    $frame = Resize-IconFrame $masterBmp $s
    if ($s -eq 256 -or $s -eq 32) {
        $frame.Save((Join-Path $scratchDir "capsule_icon_${s}.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $ms = New-Object System.IO.MemoryStream
    $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngChunks += ,($ms.ToArray())
    $ms.Dispose()
    $frame.Dispose()
}

$masterBmp.Dispose()

$icoPath = Join-Path $PSScriptRoot "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bytes = $pngChunks[$i]
    $dimByte = if ($s -ge 256) { [Byte]0 } else { [Byte]$s }
    $bw.Write($dimByte)
    $bw.Write($dimByte)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngChunks[$i])
}

$bw.Close()
$fs.Close()
Write-Output "Created Gacha Capsule $icoPath ($((Get-Item $icoPath).Length) bytes)"
