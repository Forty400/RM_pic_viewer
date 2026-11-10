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

Add-Type -AssemblyName System.IO.Compression.FileSystem

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

function New-ZipFromDirectory([string]$SourceDir, [string]$ZipPath, [bool]$includeBaseDirectory) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    # Copy first to avoid AV/file locks on freshly published EXEs.
    $stage = Join-Path $env:TEMP ("rpg-zip-" + [guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Force -Path $stage | Out-Null
        Copy-Item (Join-Path $SourceDir "*") $stage -Recurse -Force
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $stage,
            $ZipPath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $includeBaseDirectory)
    }
    finally {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
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

# Uninstall.exe is created at install time as a hard link to the Viewer (one runtime copy).
# Portable zip ships Viewer only; users delete the folder to remove.

$howTo = @"
【如何启动】
1. 双击本目录下的 RpgMakerImageViewer.exe
2. 或使用桌面 / 开始菜单快捷方式

说明：shell 是缩略图组件，一般不用打开。
安装版卸载请运行 Uninstall.exe（与主程序硬链接，不额外占磁盘）。
"@
Write-TextFile (Join-Path $payloadStage "HowToStart.txt") $howTo

Write-Host "Packing payload.zip for embedded Setup ..."
New-Item -ItemType Directory -Force -Path $embeddedDir | Out-Null
New-ZipFromDirectory $payloadStage $embeddedZip $false
Write-Host ("  payload.zip: " + (Format-Mb (Get-Item $embeddedZip).Length))

Write-Host "Publishing Setup.exe (single-file + embedded payload, compressed) ..."
$setupBuildOut = Join-Path $env:TEMP ("rpg-setup-out-" + [guid]::NewGuid().ToString("N"))
dotnet publish $setupProj -c $Configuration -r $Runtime --self-contained true `
    @sfProps `
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
if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
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
    New-ZipFromDirectory $portable $zipPortable $true
    $setupZipStage = Join-Path $distRoot "_setup_zip"
    if (Test-Path $setupZipStage) { Remove-Item $setupZipStage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $setupZipStage | Out-Null
    Copy-Item (Join-Path $setupOutDir $setupExeName) $setupZipStage -Force
    Copy-Item (Join-Path $setupOutDir "README.txt") $setupZipStage -Force
    New-ZipFromDirectory $setupZipStage $zipSetup $false
    Remove-Item $setupZipStage -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "ZIP portable: $zipPortable (" (Format-Mb (Get-Item $zipPortable).Length) ")"
    Write-Host "ZIP setup:    $zipSetup (" (Format-Mb (Get-Item $zipSetup).Length) ")"
}

Remove-Item $payloadStage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $embeddedZip -Force -ErrorAction SilentlyContinue

# Repo-root 7z for GitHub main (<100MB). Prefer 7-Zip; skip if unavailable.
$repoSetup7z = Join-Path $root "RpgMakerImageViewer.Setup.7z"
$sevenZip = @(
    "${env:ProgramFiles}\7-Zip\7z.exe",
    "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($sevenZip) {
    Write-Host "Packing repo-root Setup.7z (mx=9) ..."
    $zStage = Join-Path $env:TEMP ("rpg-setup7z-" + [guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Force -Path $zStage | Out-Null
        Copy-Item (Join-Path $distRoot $setupExeName) (Join-Path $zStage $setupExeName) -Force
        Copy-Item (Join-Path $setupOutDir "README.txt") (Join-Path $zStage "README.txt") -Force
        if (Test-Path $repoSetup7z) { Remove-Item $repoSetup7z -Force }
        & $sevenZip a -t7z -mx=9 $repoSetup7z (Join-Path $zStage "*") | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7z packing failed." }
        Write-Host ("  " + $repoSetup7z + "  (" + (Format-Mb (Get-Item $repoSetup7z).Length) + ")")
    }
    finally {
        if (Test-Path $zStage) { Remove-Item $zStage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
else {
    Write-Warning "7-Zip not found; skipped RpgMakerImageViewer.Setup.7z (needed for GitHub main <100MB)."
}

Write-Host ""
Write-Host "Single-file Setup (move anywhere):"
Write-Host ("  " + (Join-Path $distRoot $setupExeName) + "  (" + (Format-Mb (Get-Item (Join-Path $distRoot $setupExeName)).Length) + ")")
Write-Host "Portable folder:"
Write-Host ("  " + $portable + "  (" + (Format-Mb ((Get-ChildItem $portable -Recurse -File | Measure-Object Length -Sum).Sum)) + ")")
Write-Host "Done."
