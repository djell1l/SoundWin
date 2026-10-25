# -------------------------------------------------------------
# SoundWin — Script d'installation native locale Windows
# Installe dans %LOCALAPPDATA%\Programs\SoundWin
# Crée les raccourcis Bureau + Menu Démarrer avec icône
# Enregistre l'application dans Windows Applications & Fonctionnalités
# -------------------------------------------------------------
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

Write-Host "==========================================" -ForegroundColor Magenta
Write-Host " Installation Native de SoundWin (Edge) " -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

$installDir = "$env:LOCALAPPDATA\Programs\SoundWin"
$releaseBinDir = ".\src\SoundWin.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

# 1. Compilation Release
Write-Host "`n1. Compilation Release x64..." -ForegroundColor Cyan
$appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }
if ($appxTools) {
    $env:AppxMSBuildToolsPath = $appxTools
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = if (Test-Path $vswhere) {
    & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
} else { $null }

if (-not $msbuild) { throw "MSBuild.exe introuvable." }

& $msbuild "SoundWin.sln" /p:Configuration=Release /p:Platform=x64 /restore /m
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation Release" }

# Fermer toute instance en cours
Get-Process -Name "SoundWin.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2. Déploiement dans %LOCALAPPDATA%\Programs\SoundWin
Write-Host "`n2. Déploiement des fichiers dans $installDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

Copy-Item -Path "$releaseBinDir\*" -Destination $installDir -Recurse -Force
Copy-Item -Path ".\app.ico" -Destination "$installDir\app.ico" -Force

$installedExe = "$installDir\SoundWin.App.exe"
$installedIco = "$installDir\app.ico"

if (-not (Test-Path $installedExe)) {
    throw "Exécutable introuvable dans $installedExe après copie !"
}

# 3. Création des Raccourcis Windows (Bureau & Menu Démarrer)
Write-Host "`n3. Création des raccourcis Windows avec l'icône..." -ForegroundColor Cyan
$wsh = New-Object -ComObject WScript.Shell

# Raccourci Bureau
$desktopPath = [System.Environment]::GetFolderPath('Desktop')
$desktopLnk = Join-Path $desktopPath "SoundWin.lnk"
$shortcut = $wsh.CreateShortcut($desktopLnk)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installedIco,0"
$shortcut.Description = "SoundWin - Client SoundCloud Natif Windows 11"
$shortcut.Save()
Write-Host "   -> Raccourci Bureau créé : $desktopLnk" -ForegroundColor Green

# Raccourci Menu Démarrer
$startMenuPath = [System.Environment]::GetFolderPath('Programs')
$startMenuLnk = Join-Path $startMenuPath "SoundWin.lnk"
$shortcut2 = $wsh.CreateShortcut($startMenuLnk)
$shortcut2.TargetPath = $installedExe
$shortcut2.WorkingDirectory = $installDir
$shortcut2.IconLocation = "$installedIco,0"
$shortcut2.Description = "SoundWin - Client SoundCloud Natif Windows 11"
$shortcut2.Save()
Write-Host "   -> Raccourci Menu Démarrer créé : $startMenuLnk" -ForegroundColor Green

# 4. Enregistrement dans Windows Applications & Fonctionnalités (Registre)
Write-Host "`n4. Enregistrement dans le Registre Windows..." -ForegroundColor Cyan
$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SoundWin"
New-Item -Path $regKey -Force | Out-Null
Set-ItemProperty -Path $regKey -Name "DisplayName" -Value "SoundWin (Edge)"
Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value "0.1.0-edge.1"
Set-ItemProperty -Path $regKey -Name "Publisher" -Value "djell1l"
Set-ItemProperty -Path $regKey -Name "DisplayIcon" -Value $installedIco
Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "powershell.exe -Command `"Remove-Item -Recurse -Force '$installDir'; Remove-Item -Force '$desktopLnk', '$startMenuLnk', '$regKey'`""
Write-Host "   -> Application enregistrée sous Windows !" -ForegroundColor Green

# 5. Lancement direct dans la session interactive
if (-not $NoLaunch) {
    Write-Host "`n5. Lancement de SoundWin..." -ForegroundColor Cyan
    # explorer.exe garantit un lancement interactif au premier plan utilisateur
    Start-Process -FilePath "explorer.exe" -ArgumentList "`"$installedExe`""
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " Installation terminée avec succès ! " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
