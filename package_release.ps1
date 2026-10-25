param(
    [string]$Version = "0.1.0-edge.1",
    [ValidateSet("Stable", "Edge")]
    [string]$Channel = "Edge",
    [switch]$PublishGitHub,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = 'Stop'

if ($Channel -eq "Edge" -and $Version -notmatch "edge") {
    $version = "$Version-edge.1"
} else {
    $version = $Version
}

Write-Host "Packaging SoundWin v$version (Channel: $Channel)..." -ForegroundColor Magenta
$appReleaseDir = "src\SoundWin.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

# 1. Restore NuGet
Write-Host "1. Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore .\SoundWin.sln

# 2. Locate Appx tools and MSBuild
$appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }

if ($appxTools) {
    $env:AppxMSBuildToolsPath = $appxTools
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = if (Test-Path $vswhere) {
    & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
} else { $null }

if (-not $msbuild) { throw "MSBuild.exe not found." }

Write-Host "2. Building SoundWin.App (Release x64 Self-Contained)..." -ForegroundColor Cyan
& $msbuild "src\SoundWin.App\SoundWin.App.csproj" /p:Configuration=Release /p:Platform=x64 /p:Version=$version /p:PackageVersion=$version /p:InformationalVersion=$version /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed" }

# 3. Clean up satellite languages
$knownLangs = @("fr-FR", "fr", "en-US", "en")
Get-ChildItem -Directory $appReleaseDir | Where-Object { $_.Name -match "^[a-z]{2}(-[A-Z]{2,4})?$" -and $_.Name -notin $knownLangs } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# 4. Create Dist Directory & Portable Zip
Write-Host "3. Creating portable release zip..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "dist" | Out-Null
$portableZip = "dist\SoundWin-v$version-win-x64-portable.zip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }

Compress-Archive -Path "$appReleaseDir\*" -DestinationPath $portableZip -CompressionLevel Optimal
Write-Host "   -> Portable package generated: $portableZip" -ForegroundColor Green

# 5. Auto-update local machine installation if installed
$localInstallDir = "$env:LOCALAPPDATA\Programs\SoundWin"
if (Test-Path $localInstallDir) {
    Write-Host "4. Updating local installation ($localInstallDir)..." -ForegroundColor Cyan
    Get-Process -Name SoundWin.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Copy-Item -Recurse -Force "$appReleaseDir\*" "$localInstallDir\"
    Write-Host "   -> Local installation updated successfully!" -ForegroundColor Green
}

# 6. Optional / Automatic GitHub Release Publishing
if ($PublishGitHub) {
    Write-Host "5. Publishing GitHub Release..." -ForegroundColor Magenta
    $tag = "v$version"
    $title = if ($Channel -eq "Edge") { "SoundWin v$version [Bleeding Edge]" } else { "SoundWin v$version [Stable]" }
    $target = if ($Channel -eq "Edge") { "edge" } else { "main" }
    
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "SoundWin v$version release.`n`n- Pure WinUI 3 & C# (.NET 8) native desktop client`n- Hardware accelerated streaming & dynamic waveform scrubbing`n- SMTC & Discord Rich Presence integration"
    }

    $ghArgs = @("release", "create", $tag, $portableZip, "--repo", "djell1l/SoundWin", "--target", $target, "--title", $title, "--notes", $ReleaseNotes)
    if ($Channel -eq "Edge") {
        $ghArgs += "--prerelease"
    }

    & gh @ghArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   -> Release $tag published successfully to GitHub!" -ForegroundColor Green
    } else {
        Write-Warning "   -> Failed to publish release via gh CLI."
    }
}
