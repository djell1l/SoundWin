param(
    [string]$Version = "0.1.0-edge.2",
    [ValidateSet("Stable", "Edge")]
    [string]$Channel = "Edge",
    [switch]$PublishGitHub,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = 'Stop'

if ($Channel -eq "Edge" -and $Version -notmatch "edge") {
    $version = "$Version-edge.2"
} else {
    $version = $Version
}

Write-Host "Packaging SCwin v$version (Canal: $Channel)..." -ForegroundColor Magenta
$appReleaseDir = "src\SoundWin.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
$installerReleaseDir = "src\SoundWin.Installer\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

# 0. Restore NuGet packages
Write-Host "0. Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore .\SoundWin.sln

# 1. Locate Appx tools and MSBuild
$appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\2022" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }

if (-not $appxTools) {
    $appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
        Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }
}

$msbuild = $null
if ($appxTools) {
    $env:AppxMSBuildToolsPath = $appxTools
    $vsDir = $appxTools
    for ($i = 0; $i -lt 4; $i++) { $vsDir = Split-Path $vsDir }
    $candidateMsBuild = Join-Path $vsDir "Current\Bin\MSBuild.exe"
    if (Test-Path $candidateMsBuild) {
        $msbuild = $candidateMsBuild
    }
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = if (Test-Path $vswhere) {
        & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    } else { $null }
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "MSBuild.exe introuvable. Assurez-vous que Visual Studio 2022 ou les Build Tools sont installés."
}

$csc = $null
$roslynCsc = Join-Path (Split-Path $msbuild) "Roslyn\csc.exe"
if (Test-Path $roslynCsc) {
    $csc = $roslynCsc
} else {
    $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

Write-Host "Utilisation de MSBuild : $msbuild" -ForegroundColor Cyan
Write-Host "Utilisation de CSC     : $csc" -ForegroundColor Cyan

# 2. Compile SoundWin.App
Write-Host "1. Building SoundWin.App (Release x64 Self-Contained)..."
& $msbuild "src\SoundWin.App\SoundWin.App.csproj" /p:Configuration=Release /p:Platform=x64 /p:Version=$version /p:PackageVersion=$version /p:InformationalVersion=$version /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build SoundWin.App failed." }

# 3. Compile SoundWin.Installer
Write-Host "2. Building SoundWin.Installer (WinUI 3 Setup.exe)..."
& $msbuild "src\SoundWin.Installer\SoundWin.Installer.csproj" /p:Configuration=Release /p:Platform=x64 /p:Version=$version /p:PackageVersion=$version /p:InformationalVersion=$version /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build SoundWin.Installer failed." }

# Clean up satellite culture folders
$knownLangs = @("fr-FR", "fr", "en-US", "en")
Get-ChildItem -Directory $appReleaseDir | Where-Object { $_.Name -match "^[a-z]{2}(-[A-Z]{2,4})?$" -and $_.Name -notin $knownLangs } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# 4. Prepare Deduplicated Installer Bundle
Write-Host "3. Preparing Deduplicated Installer Bundle..."
if (Test-Path "dist\InstallerBundle") {
    Remove-Item -Recurse -Force "dist\InstallerBundle"
}
New-Item -ItemType Directory -Force -Path "dist\InstallerBundle\AppXbf" | Out-Null

Copy-Item -Recurse -Force "$appReleaseDir\*" "dist\InstallerBundle\"

if (Test-Path "$appReleaseDir\App.xbf") {
    Copy-Item -Force "$appReleaseDir\App.xbf" "dist\InstallerBundle\AppXbf\App.xbf"
}

Copy-Item -Force "$installerReleaseDir\Setup.*" "dist\InstallerBundle\"
Copy-Item -Force "$installerReleaseDir\App.xbf" "dist\InstallerBundle\App.xbf"
Copy-Item -Force "$installerReleaseDir\MainWindow.xbf" "dist\InstallerBundle\MainWindow.xbf"
Copy-Item -Force "$installerReleaseDir\AppIcon.png" "dist\InstallerBundle\AppIcon.png"

# 5. Packaging Portable Zip
Write-Host "4. Packaging Portable Zip..."
$portableZip = "dist\SCwin-v$version-win-x64-portable.zip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
Compress-Archive -Path "$appReleaseDir\*" -DestinationPath $portableZip -CompressionLevel Optimal

# 6. Building Single-File WinUI 3 Setup.exe
Write-Host "5. Building Single-File WinUI 3 Setup.exe..."
$bundleZip = "dist\bundle_temp.zip"
if (Test-Path $bundleZip) { Remove-Item -Force $bundleZip }
Compress-Archive -Path "dist\InstallerBundle\*" -DestinationPath $bundleZip -CompressionLevel Optimal

$stubExe = "dist\stub_temp.exe"
if (Test-Path $stubExe) { Remove-Item -Force $stubExe }
& $csc /target:winexe /optimize+ /platform:x64 /win32icon:"src\SoundWin.Installer\app.ico" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:$stubExe "src\SoundWin.Installer\Bootstrapper.cs"
if ($LASTEXITCODE -ne 0) { throw "CSC compilation of Bootstrapper failed." }

$finalSetupExe = "dist\SCwin-Setup-v$version-win-x64.exe"
if (Test-Path $finalSetupExe) { Remove-Item -Force $finalSetupExe }

$stubBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $stubExe).Path)
$zipInfo = Get-Item $bundleZip
$zipLength = [int64]$zipInfo.Length
$lenBytes = [System.BitConverter]::GetBytes($zipLength)
$magicBytes = [System.Text.Encoding]::ASCII.GetBytes("SOUNDWINSFX")

$outStream = [System.IO.File]::OpenWrite((Join-Path (Get-Location).Path $finalSetupExe))
try {
    $outStream.Write($stubBytes, 0, $stubBytes.Length)
    $inZip = [System.IO.File]::OpenRead($zipInfo.FullName)
    try {
        $inZip.CopyTo($outStream)
    } finally {
        $inZip.Close()
    }
    $outStream.Write($lenBytes, 0, 8)
    $outStream.Write($magicBytes, 0, 11)
} finally {
    $outStream.Close()
}

Remove-Item -Force $bundleZip -ErrorAction SilentlyContinue
Remove-Item -Force $stubExe -ErrorAction SilentlyContinue

Write-Host "Done! Artifacts in dist\:" -ForegroundColor Green
Get-ChildItem "dist\SoundWin*v$version*" | Select-Object Name, @{Name="SizeMB";Expression={[math]::Round($_.Length/1MB, 2)}}, LastWriteTime

# 7. Auto-update local machine installation
$localInstallDir = "$env:LOCALAPPDATA\Programs\SoundWin"
if (Test-Path $localInstallDir) {
    Write-Host "Updating local installation ($localInstallDir)..." -ForegroundColor Cyan
    Get-Process -Name SoundWin.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Copy-Item -Recurse -Force "$appReleaseDir\*" "$localInstallDir\"
    Write-Host "Local installation updated successfully!" -ForegroundColor Green
}

# 8. Copy Setup to user desktop
$desktopDir = [System.Environment]::GetFolderPath('Desktop')
$desktopSetupsDir = Join-Path $desktopDir "SCwin Setups"
if (-not (Test-Path $desktopSetupsDir)) {
    New-Item -ItemType Directory -Path $desktopSetupsDir -Force | Out-Null
}

if (Test-Path $finalSetupExe) {
    Copy-Item -Force $finalSetupExe (Join-Path $desktopDir "SCwin-Setup-Latest.exe")
    Copy-Item -Force $finalSetupExe (Join-Path $desktopSetupsDir "SCwin-Setup-v$version-win-x64.exe")
    Write-Host "Setup copied to Desktop and '$desktopSetupsDir'!" -ForegroundColor Green
}

# 9. Optional / Automatic GitHub Release Publishing
if ($PublishGitHub) {
    Write-Host "Publishing GitHub Release..." -ForegroundColor Magenta
    $tag = "v$version"
    $title = if ($Channel -eq "Edge") { "SCwin v$version [Bleeding Edge]" } else { "SCwin v$version [Stable]" }
    $target = if ($Channel -eq "Edge") { "edge" } else { "main" }
    
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "SoundWin v$version release.`n`n- Single-file WinUI 3 setup installer architecture`n- Standalone portable zip package`n- Pure WinUI 3 & C# (.NET 8) native desktop client`n- Hardware accelerated streaming & dynamic waveform scrubbing`n- SMTC & Discord Rich Presence integration"
    }

    $ghArgs = @("release", "create", $tag, $finalSetupExe, $portableZip, "--repo", "djell1l/SoundWin", "--target", $target, "--title", $title, "--notes", $ReleaseNotes)
    if ($Channel -eq "Edge") {
        $ghArgs += "--prerelease"
    }

    & gh @ghArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Release $tag published successfully to GitHub with Setup.exe and Portable.zip!" -ForegroundColor Green
    } else {
        Write-Warning "Failed to publish release via gh CLI."
    }
}
