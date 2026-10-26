Add-Type -AssemblyName System.Drawing

$sourcePath = "C:\Users\djell\.gemini\antigravity\brain\eb734ed7-9a99-4dc0-b623-cc1fb03f4a6c\.user_uploaded\media_1792955357022_d78f5f2f.jpg"
if (-not (Test-Path $sourcePath)) {
    throw "Source logo not found at $sourcePath"
}

Write-Host "Loading new logo from $sourcePath..." -ForegroundColor Cyan
$srcRaw = [System.Drawing.Image]::FromFile($sourcePath)

# 1. Crop to square (733x733)
$minDim = [Math]::Min($srcRaw.Width, $srcRaw.Height)
$cropX = [int](($srcRaw.Width - $minDim) / 2)
$cropY = [int](($srcRaw.Height - $minDim) / 2)

$srcSquare = New-Object System.Drawing.Bitmap($minDim, $minDim, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gCrop = [System.Drawing.Graphics]::FromImage($srcSquare)
$gCrop.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gCrop.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$gCrop.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$gCrop.DrawImage($srcRaw, [System.Drawing.Rectangle]::new(0, 0, $minDim, $minDim), [System.Drawing.Rectangle]::new($cropX, $cropY, $minDim, $minDim), [System.Drawing.GraphicsUnit]::Pixel)
$gCrop.Dispose()
$srcRaw.Dispose()

# Helper function to create rounded-corner bitmap
function Create-RoundedImage($source, [int]$size, [float]$radiusPercent = 0.22) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $r = $size * $radiusPercent
    $d = $r * 2.0
    $rect = [System.Drawing.RectangleF]::new(0, 0, $size, $size)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $g.SetClip($path)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.ResetClip()

    # Subtle anti-aliased border highlight
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(35, 255, 255, 255), [Math]::Max(1.0, [float]($size / 256.0)))
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()
    $g.Dispose()

    return $bmp
}

$assetsDir = "C:\Users\djell\Projects\SoundWin\src\SoundWin.App\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

Write-Host "Generating rounded high-res icons..." -ForegroundColor Cyan

# 1024x1024 for AppIcon and IntroLogo
$img1024 = Create-RoundedImage $srcSquare 1024 0.22
$img1024.Save("$assetsDir\AppIcon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img1024.Save("$assetsDir\IntroLogo.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img1024.Save("C:\Users\djell\Projects\SoundWin\src\SoundWin.Installer\AppIcon.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 256x256
$img256 = Create-RoundedImage $srcSquare 256 0.22
$img256.Save("$assetsDir\AppIcon256.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 300x300 & 150x150
$img300 = Create-RoundedImage $srcSquare 300 0.22
$img300.Save("$assetsDir\Square150x150Logo.scale-200.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img150 = Create-RoundedImage $srcSquare 150 0.22
$img150.Save("$assetsDir\Square150x150Logo.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 88x88, 48x48, 44x44, 24x24
$img88 = Create-RoundedImage $srcSquare 88 0.22
$img88.Save("$assetsDir\Square44x44Logo.scale-200.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img44 = Create-RoundedImage $srcSquare 44 0.22
$img44.Save("$assetsDir\Square44x44Logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img24 = Create-RoundedImage $srcSquare 24 0.22
$img24.Save("$assetsDir\Square44x44Logo.targetsize-24_altform-unplated.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img50 = Create-RoundedImage $srcSquare 50 0.22
$img50.Save("$assetsDir\StoreLogo.png", [System.Drawing.Imaging.ImageFormat]::Png)

# Multi-res ICO file for Windows
Write-Host "Generating multi-resolution rounded app.ico..." -ForegroundColor Cyan
$sizes = @(256, 128, 64, 48, 32, 16)
$bitmaps = @()
foreach ($s in $sizes) {
    $bitmaps += Create-RoundedImage $srcSquare $s 0.22
}

$icoPath = "C:\Users\djell\Projects\SoundWin\src\SoundWin.App\app.ico"
$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($stream)

$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$pngBytesList = @()
foreach ($bmp in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList += ,$ms.ToArray()
    $ms.Dispose()
}

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w = if ($s -ge 256) { 0 } else { [byte]$s }
    $h = if ($s -ge 256) { 0 } else { [byte]$s }
    $bytes = $pngBytesList[$i]
    
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bytes = $pngBytesList[$i]
    $writer.Write($bytes)
}

$writer.Flush()
$stream.Close()

Copy-Item $icoPath "$assetsDir\app.ico" -Force
Copy-Item $icoPath "C:\Users\djell\Projects\SoundWin\src\SoundWin.Installer\app.ico" -Force
Copy-Item $icoPath "C:\Users\djell\Projects\SoundWin\app.ico" -Force

# Clean up
$srcSquare.Dispose()
$img1024.Dispose()
$img256.Dispose()
$img300.Dispose()
$img150.Dispose()
$img88.Dispose()
$img44.Dispose()
$img24.Dispose()
$img50.Dispose()
foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Rounded icons and ICO generated successfully from user logo!" -ForegroundColor Green
