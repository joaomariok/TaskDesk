#requires -Version 7
# Prints the CHANGELOG.md section of one version (Keep a Changelog 1.1.0, #144): the lines under
# "## [<Version>] - YYYY-MM-DD" up to the next "## " heading, without the heading and trimmed. It prints
# nothing when the file has no section for the version. The release workflow checks with it before anything
# else, and scripts/pack-installer.ps1 passes the section to Velopack as the release notes.
#   -Version <x.y.z>  the version to find
#   -Path <file>      the changelog; default: CHANGELOG.md at the repository root
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Path
)
$ErrorActionPreference = 'Stop'
if (-not $Path) { $Path = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md' }
if (-not (Test-Path $Path)) { return }
$heading = '^## \[' + [regex]::Escape($Version) + '\] - \d{4}-\d{2}-\d{2}\s*$'
$lines = [System.Collections.Generic.List[string]]::new()
$inSection = $false
foreach ($line in Get-Content $Path) {
    if ($inSection) {
        if ($line -match '^## ') { break }
        $lines.Add($line)
    }
    elseif ($line -match $heading) { $inSection = $true }
}
$text = ($lines -join "`n").Trim()
if ($text) { $text }
