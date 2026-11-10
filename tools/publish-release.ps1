param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $root "dist"
$portable = Join-Path $distRoot "RpgMakerImageViewer"
$payloadStage = Join-Path $distRoot "_payload"
$setupOutDir = Join-Path $distRoot "Setup"
$setupExeName = "RpgMakerImageViewer.Setup.exe"
$zipPortable = Join-Path $distRoot "RpgMakerImageViewer-win-x64.zip"
$zipSetup = Join-Path $distRoot "RpgMakerImageViewer-Setup-win-x64.zip"
$viewerProj = Join-Path $root "src\RpgMaker.Viewer\RpgMaker.Viewer.csproj"
$thumbProj = Join-Path $root "src\RpgMaker.ThumbnailProvider\RpgMaker.ThumbnailProvider.csproj"
$setupProj = Join-Path $root "src\RpgMaker.Setup\RpgMaker.Setup.csproj"
$embeddedDir = Join-Path $root "src\RpgMaker.Setup\embedded"
$embeddedZip = Join-Path $embeddedDir "payload.zip"

# Shared single-file flags: one runtime copy + LZ compression (major size win).
$sfProps = @(
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none",
    "-p:DebuggerSupport=false"
)

function Write-TextFile([string]$Path, [string]$Content) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Format-Mb([long]$bytes) {
    return ("{0:N1} MB" -f ($bytes / 1MB))
}

Write-Host "Cleaning dist..."
if (Test-Path $distRoot) {
    Get-ChildItem $distRoot -Force | ForEach-Object {
        try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Skip locked: $($_.FullName)" }
    }
}
if (Test-Path $embeddedZip) { Remove-Item $embeddedZip -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $payloadStage | Out-Null

Write-Host "Publishing Viewer (single-file, compressed) -> payload root ..."
$viewerOut = Join-Path $env:TEMP ("rpg-viewer-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $viewerProj -c $Configuration -r $Runtime --self-contained true `
    @sfProps `
    -o $viewerOut
if ($LASTEXITCODE -ne 0) { throw "Viewer publish failed." }
$viewerExe = Get-ChildItem $viewerOut -Filter "RpgMakerImageViewer.exe" | Select-Object -First 1
if (-not $viewerExe) { throw "Viewer exe not produced." }
Copy-Item $viewerExe.FullName (Join-Path $payloadStage "RpgMakerImageViewer.exe") -Force
Write-Host ("  Viewer: " + (Format-Mb $viewerExe.Length))
Remove-Item $viewerOut -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing thumbnail provider -> payload\shell ..."
dotnet publish $thumbProj -c $Configuration -r $Runtime --self-contained false `
    -o (Join-Path $payloadStage "shell")
if ($LASTEXITCODE -ne 0) { throw "ThumbnailProvider publish failed." }

Write-Host "Publishing Uninstall.exe (single-file, compressed, no payload embed) ..."
$uninstallOut = Join-Path $env:TEMP ("rpg-uninstall-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $setupProj -c $Configuration -r $Runtime --self-contained true `
    @sfProps `
    -p:AssemblyName=Uninstall `
    -o $uninstallOut
if ($LASTEXITCODE -ne 0) { throw "Uninstall publish failed." }
$builtUninstall = Get-ChildItem $uninstallOut -Filter "Uninstall.exe" | Select-Object -First 1
if (-not $builtUninstall) { throw "Uninstall.exe not produced." }
Copy-Item $builtUninstall.FullName (Join-Path $payloadStage "Uninstall.exe") -Force
Write-Host ("  Uninstall: " + (Format-Mb $builtUninstall.Length))
Remove-Item $uninstallOut -Recurse -Force -ErrorAction SilentlyContinue

$howTo = @"
【如何启动】
1. 双击本目录下的 RpgMakerImageViewer.exe
2. 或使用桌面 / 开始菜单快捷方式

说明：shell 是缩略图组件，一般不用打开。
"@
Write-TextFile (Join-Path $payloadStage "HowToStart.txt") $howTo

Write-Host "Packing payload.zip for embedded Setup ..."
New-Item -ItemType Directory -Force -Path $embeddedDir | Out-Null
if (Test-Path $embeddedZip) { Remove-Item $embeddedZip -Force }
Compress-Archive -Path (Join-Path $payloadStage "*") -DestinationPath $embeddedZip -Force
Write-Host ("  payload.zip: " + (Format-Mb (Get-Item $embeddedZip).Length))

Write-Host "Publishing Setup.exe (single-file + embedded payload, compressed) ..."
$setupBuildOut = Join-Path $env:TEMP ("rpg-setup-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $setupProj -c $Configuration -r $Runtime --self-contained true `
    @sfProps `
    -p:AssemblyName=RpgMakerImageViewer.Setup `
    -o $setupBuildOut
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed." }

New-Item -ItemType Directory -Force -Path $setupOutDir | Out-Null
$builtSetup = Get-ChildItem $setupBuildOut -Filter $setupExeName | Select-Object -First 1
if (-not $builtSetup) { throw "$setupExeName not produced." }
Copy-Item $builtSetup.FullName (Join-Path $setupOutDir $setupExeName) -Force
Copy-Item $builtSetup.FullName (Join-Path $distRoot $setupExeName) -Force
Write-Host ("  Setup: " + (Format-Mb $builtSetup.Length))
Remove-Item $setupBuildOut -Recurse -Force -ErrorAction SilentlyContinue

$setupReadme = @"
RPG Maker Image Viewer - Setup
==============================

直接双击 RpgMakerImageViewer.Setup.exe 即可安装。
本文件为单文件安装包（已内嵌程序数据），可单独复制到任意目录运行。
"@
Write-TextFile (Join-Path $setupOutDir "README.txt") $setupReadme
Write-TextFile (Join-Path $distRoot "README-SETUP.txt") $setupReadme

Write-Host "Creating portable folder..."
New-Item -ItemType Directory -Force -Path $portable | Out-Null
Copy-Item (Join-Path $payloadStage "*") $portable -Recurse -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $portable "README.md") -Force
Write-TextFile (Join-Path $portable "HowToStart.txt") $howTo

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
    $setupZipStage = Join-Path $distRoot "_setup_zip"
    if (Test-Path $setupZipStage) { Remove-Item $setupZipStage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $setupZipStage | Out-Null
    Copy-Item (Join-Path $setupOutDir $setupExeName) $setupZipStage -Force
    Copy-Item (Join-Path $setupOutDir "README.txt") $setupZipStage -Force
    Compress-Archive -Path (Join-Path $setupZipStage "*") -DestinationPath $zipSetup -Force
    Remove-Item $setupZipStage -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "ZIP portable: $zipPortable (" (Format-Mb (Get-Item $zipPortable).Length) ")"
    Write-Host "ZIP setup:    $zipSetup (" (Format-Mb (Get-Item $zipSetup).Length) ")"
}

Remove-Item $payloadStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $embeddedZip -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Single-file Setup (move anywhere):"
Write-Host ("  " + (Join-Path $distRoot $setupExeName) + "  (" + (Format-Mb (Get-Item (Join-Path $distRoot $setupExeName)).Length) + ")")
Write-Host "Done."
