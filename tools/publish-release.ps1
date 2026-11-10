param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $root "dist"
$portable = Join-Path $distRoot "RpgMakerImageViewer"
$installer = Join-Path $distRoot "Installer"
$payload = Join-Path $installer "payload"
$payloadBin = Join-Path $payload "bin"
$zipPortable = Join-Path $distRoot "RpgMakerImageViewer-win-x64.zip"
$zipSetup = Join-Path $distRoot "RpgMakerImageViewer-Setup-win-x64.zip"
$viewerProj = Join-Path $root "src\RpgMaker.Viewer\RpgMaker.Viewer.csproj"
$launcherProj = Join-Path $root "src\RpgMaker.Launcher\RpgMaker.Launcher.csproj"
$thumbProj = Join-Path $root "src\RpgMaker.ThumbnailProvider\RpgMaker.ThumbnailProvider.csproj"
$setupProj = Join-Path $root "src\RpgMaker.Setup\RpgMaker.Setup.csproj"

function Write-TextFile([string]$Path, [string]$Content) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

Write-Host "Cleaning dist..."
if (Test-Path $distRoot) {
    Get-ChildItem $distRoot -Force | ForEach-Object {
        try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Skip locked: $($_.FullName)" }
    }
}
New-Item -ItemType Directory -Force -Path $payloadBin | Out-Null

Write-Host "Publishing viewer -> payload\bin ..."
dotnet publish $viewerProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false `
    -o $payloadBin
if ($LASTEXITCODE -ne 0) { throw "Viewer publish failed." }

Write-Host "Publishing launcher -> payload root ..."
$launcherOut = Join-Path $env:TEMP ("rpg-launcher-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $launcherProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $launcherOut
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }
Copy-Item (Join-Path $launcherOut "RpgMakerImageViewer.exe") (Join-Path $payload "RpgMakerImageViewer.exe") -Force
Remove-Item $launcherOut -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing thumbnail provider -> payload\shell ..."
dotnet publish $thumbProj -c $Configuration -r $Runtime --self-contained false -o (Join-Path $payload "shell")
if ($LASTEXITCODE -ne 0) { throw "ThumbnailProvider publish failed." }

Write-Host "Publishing setup (folder) -> Installer ..."
dotnet publish $setupProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false `
    -o $installer
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed." }

# Single-file Uninstall.exe goes into payload so install dir gets a self-contained uninstaller.
Write-Host "Publishing single-file Uninstall.exe -> payload ..."
$uninstallOut = Join-Path $env:TEMP ("rpg-uninstall-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $setupProj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:AssemblyName=Uninstall `
    -o $uninstallOut
if ($LASTEXITCODE -ne 0) { throw "Uninstall publish failed." }
$builtUninstall = Get-ChildItem $uninstallOut -Filter "Uninstall.exe" | Select-Object -First 1
if (-not $builtUninstall) { throw "Uninstall.exe not produced." }
Copy-Item $builtUninstall.FullName (Join-Path $payload "Uninstall.exe") -Force
Remove-Item $uninstallOut -Recurse -Force -ErrorAction SilentlyContinue

$howTo = @"
【如何启动】
1. 双击本目录下的 RpgMakerImageViewer.exe
2. 或使用桌面 / 开始菜单快捷方式

说明：bin、shell 是内部文件，一般不用打开。
"@
Write-TextFile (Join-Path $payload "HowToStart.txt") $howTo

Write-Host "Creating portable folder..."
New-Item -ItemType Directory -Force -Path $portable | Out-Null
Copy-Item (Join-Path $payload "*") $portable -Recurse -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $portable "README.md") -Force
Write-TextFile (Join-Path $portable "HowToStart.txt") $howTo

$setupReadme = @"
RPG Maker Image Viewer - Installer
==================================

1. Run RpgMakerImageViewer.Setup.exe
2. Choose install path and options
3. After install, double-click RpgMakerImageViewer.exe in the install folder
   (or use the desktop / Start Menu shortcut)

Install folder looks like:
  RpgMakerImageViewer.exe          <-- start here
  HowToStart.txt
  Uninstall.exe
  bin\                             (internal)
  shell\                           (internal)

Do not delete payload\ next to Setup.exe before installing.
"@
Write-TextFile (Join-Path $installer "README-INSTALL.txt") $setupReadme

if (-not $SkipZip) {
    foreach ($zip in @($zipPortable, $zipSetup)) {
        if (Test-Path $zip) {
            try { Remove-Item $zip -Force -ErrorAction Stop }
            catch {
                $alt = [IO.Path]::ChangeExtension($zip, ("." + (Get-Date -Format "yyyyMMddHHmmss") + ".zip"))
                Write-Warning "Locked zip, will write: $alt"
                if ($zip -eq $zipPortable) { $zipPortable = $alt } else { $zipSetup = $alt }
            }
        }
    }
    Compress-Archive -Path $portable -DestinationPath $zipPortable -Force
    Compress-Archive -Path $installer -DestinationPath $zipSetup -Force
    Write-Host "ZIP portable: $zipPortable"
    Write-Host "ZIP setup:    $zipSetup"
}

Write-Host "Installer folder: $installer"
Write-Host "Done."
