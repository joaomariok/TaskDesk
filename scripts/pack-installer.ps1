#requires -Version 7
# Builds a local Setup.exe for testing an install without cutting a release (#87, #88). It publishes the win-x64
# profile at the given version and packs it with Velopack (vpk, pinned in dotnet-tools.json).
#   -Version <x.y.z>     the version of the app and the package (SemVer 2, for example 0.2.0)
#   -OutputDir <dir>     where Setup.exe and the release packages go; default: releases\ (git-ignored)
# The package id is TaskDesk.App, never TaskDesk: uninstall deletes %LOCALAPPDATA%\<package id>, and
# %LOCALAPPDATA%\TaskDesk is the data folder (InstallerGuardrailTests). Only a Start-menu shortcut is made.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\TaskDesk.App\TaskDesk.App.csproj'
$icon = Join-Path $repoRoot 'src\TaskDesk.App\Assets\TaskDesk.ico'
# A folder of its own, emptied first, so no file of an earlier publish lands in the package.
$publishDir = Join-Path $repoRoot 'publish\pack-win-x64'
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'releases' }
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

function Invoke-Checked {
    param([string[]]$Arguments)
    Write-Host "== dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Checked @('publish', $project, '-p:PublishProfile=win-x64', "-p:Version=$Version",
    "-p:PublishDir=$publishDir\")

Push-Location $repoRoot
try {
    Invoke-Checked @('tool', 'restore')
    Invoke-Checked @('tool', 'run', 'vpk', '--', 'pack',
        '--packId', 'TaskDesk.App',
        '--packTitle', 'TaskDesk',
        '--packVersion', $Version,
        '--runtime', 'win-x64',
        '--packDir', $publishDir,
        '--mainExe', 'TaskDesk.App.exe',
        '--icon', $icon,
        '--shortcuts', 'StartMenuRoot',
        '--outputDir', $OutputDir)
}
finally {
    Pop-Location
}

Write-Host "TaskDesk.App-win-Setup.exe and the release packages for $Version are in $OutputDir"
