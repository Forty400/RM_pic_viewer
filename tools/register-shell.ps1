#Requires -RunAsAdministrator
param(
    [switch]$Unregister,
    [string]$Configuration = "Release",
    [string]$ViewerPath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$providerProject = Join-Path $root "src\RpgMaker.ThumbnailProvider\RpgMaker.ThumbnailProvider.csproj"
$clsid = "{A7B3C1D2-4E5F-6789-ABCD-EF0123456789}"
$thumbHandler = "{e357fccd-a995-4576-b01f-234630154e96}"
$extensions = @(".rpgmvp", ".png_")

function Get-OutputDir {
    return Join-Path $root "src\RpgMaker.ThumbnailProvider\bin\$Configuration\net8.0-windows"
}

function Get-ComHostPath {
    $comhost = Join-Path (Get-OutputDir) "RpgMaker.ThumbnailProvider.comhost.dll"
    if (-not (Test-Path $comhost)) {
        throw "COM host not found: $comhost. Build the ThumbnailProvider project first."
    }
    return (Resolve-Path $comhost).Path
}

function Unregister-ShellEx {
    foreach ($ext in $extensions) {
        $shellEx = "HKLM:\SOFTWARE\Classes\$ext\ShellEx\$thumbHandler"
        if (Test-Path $shellEx) { Remove-Item -Path $shellEx -Recurse -Force }

        $openCmd = "HKLM:\SOFTWARE\Classes\$ext\shell\open\command"
        if (Test-Path $openCmd) { Remove-Item -Path $openCmd -Recurse -Force }
    }
}

function Register-ShellEx {
    param([string]$ResolvedViewer)

    foreach ($ext in $extensions) {
        $shellEx = "HKLM:\SOFTWARE\Classes\$ext\ShellEx\$thumbHandler"
        New-Item -Path $shellEx -Force | Out-Null
        Set-ItemProperty -Path $shellEx -Name "(default)" -Value $clsid

        if ($ResolvedViewer) {
            $cmdKey = "HKLM:\SOFTWARE\Classes\$ext\shell\open\command"
            New-Item -Path $cmdKey -Force | Out-Null
            Set-ItemProperty -Path $cmdKey -Name "(default)" -Value "`"$ResolvedViewer`" `"%1`""
        }
    }
}

Write-Host "Building ThumbnailProvider ($Configuration)..."
dotnet build $providerProject -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$comhost = Get-ComHostPath

if ($Unregister) {
    Write-Host "regsvr32 /u ..."
    & regsvr32.exe /s /u $comhost
    Unregister-ShellEx
    Write-Host "Unregistered thumbnail provider."
} else {
    Write-Host "regsvr32 ..."
    & regsvr32.exe /s $comhost
    if ($LASTEXITCODE -ne 0) { throw "regsvr32 failed with code $LASTEXITCODE" }

    $resolvedViewer = $null
    if ($ViewerPath) {
        $resolvedViewer = (Resolve-Path $ViewerPath).Path
    }

    Register-ShellEx -ResolvedViewer $resolvedViewer
    Write-Host "Registered thumbnail provider."
    Write-Host "COM host: $comhost"
    Write-Host "Restart Explorer or clear thumbnail cache to verify Large Icons."
}
