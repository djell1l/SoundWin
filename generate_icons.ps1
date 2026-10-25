Add-Type -AssemblyName System.Drawing

$sourcePath = "C:\Users\djell\.gemini\antigravity\brain\eb734ed7-9a99-4dc0-b623-cc1fb03f4a6c\.user_uploaded\media_1792932183807_b096859c.jpg"
if (-not (Test-Path $sourcePath)) {
    throw "Source image not found at $sourcePath"
}

Write-Host "Loading image from $sourcePath..." -ForegroundColor Cyan
$src = [System.Drawing.Image]::FromFile($sourcePath)

function Resize-Image($image, $width, $height) {
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($image, 0, 0, $width, $height)
    $g.Dispose()
    return $bmp
}

$assetsDir = "C:\Users\djell\Projects\SoundWin\src\SoundWin.App\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

Write-Host "Generating upscaled PNGs..." -ForegroundColor Cyan
$img1024 = Resize-Image $src 1024 1024
$img1024.Save("$assetsDir\AppIcon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img1024.Save("$assetsDir\DefaultCover.png", [System.Drawing.Imaging.ImageFormat]::Png)

$img256 = Resize-Image $src 256 256
$img256.Save("$assetsDir\AppIcon256.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img256.Save("$assetsDir\Square150x150Logo.png", [System.Drawing.Imaging.ImageFormat]::Png)

$img48 = Resize-Image $src 48 48
$img48.Save("$assetsDir\Square44x44Logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
$img48.Save("$assetsDir\StoreLogo.png", [System.Drawing.Imaging.ImageFormat]::Png)

# Generate multi-size ICO file
Write-Host "Generating multi-resolution app.ico..." -ForegroundColor Cyan
$sizes = @(256, 128, 64, 48, 32, 16)
$bitmaps = @()
foreach ($s in $sizes) {
    $bitmaps += Resize-Image $src $s $s
}

$icoPath = "C:\Users\djell\Projects\SoundWin\src\SoundWin.App\app.ico"
$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter $stream

# ICO Header: 0, 1 (icon type), number of images
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
    $writer.Write([byte]0) # colors
    $writer.Write([byte]0) # reserved
    $writer.Write([uint16]1) # color planes
    $writer.Write([uint16]32) # bpp
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
Copy-Item $icoPath "C:\Users\djell\Projects\SoundWin\app.ico" -Force

# Clean up
$src.Dispose()
$img1024.Dispose()
$img256.Dispose()
$img48.Dispose()
foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Icons generated successfully at $icoPath!" -ForegroundColor Green
