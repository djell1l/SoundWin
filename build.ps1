<#
    Build & run SoundWin (Windows 10/11 WinUI 3).
    Usage :
        .\build.ps1              # build Debug + run
        .\build.ps1 -Configuration Release
        .\build.ps1 -NoRun
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"

$appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }

if ($appxTools) {
    Write-Host "Outils Appx : $appxTools" -ForegroundColor DarkGray
    $env:AppxMSBuildToolsPath = $appxTools
} else {
    Write-Warning "Microsoft.Build.Packaging.Pri.Tasks.dll introuvable."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = if (Test-Path $vswhere) {
    & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
} else { $null }

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "Visual Studio 2022 MSBuild.exe introuvable."
}

Write-Host "Utilisation de MSBuild : $msbuild" -ForegroundColor DarkGray

Write-Host "== Compilation ($Configuration, x64) ==" -ForegroundColor Cyan
& $msbuild "SoundWin.sln" /p:Configuration=$Configuration /p:Platform=x64 /m
if ($LASTEXITCODE -ne 0) { throw "Build échoué" }

if (-not $NoRun) {
    Write-Host "== Lancement de SoundWin ==" -ForegroundColor Cyan
    $candidates = @(
        ".\src\SoundWin.App\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\SoundWin.App.exe",
        ".\src\SoundWin.App\bin\x64\$Configuration\net8.0-windows10.0.19041.0\SoundWin.App.exe"
    )
    $exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($exe) {
        Write-Host "Démarrage : $exe" -ForegroundColor Green
        Start-Process $exe
    } else {
        Write-Warning "Exécutable introuvable."
    }
}
