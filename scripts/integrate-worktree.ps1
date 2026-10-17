#requires -Version 7
<#
.SYNOPSIS
Carries a worktree's uncommitted changes onto the integration checkout, then runs the gate.

.DESCRIPTION
Copies the worktree's tracked changes relative to -Base (git diff --binary, applied with
git apply --3way in the target) and its untracked, non-ignored files. On success it runs
scripts/gate.ps1 in the target, prints the changed and new files, and exits with the gate's
exit code.

The caller commits. This script never commits, resets or checks out, and stages only what
git apply --3way stages itself. Untracked files are copied but left unstaged. The caller
stages the printed paths by name.

Exit codes:
  0   gate passed
  2   pre-check failed (nothing was changed)
  3   apply left conflicts (conflicted files listed; resolution is left to a merger)
  4   apply failed without conflicts (nothing was changed)
  any other value is the gate's own exit code

.PARAMETER Worktree
Path of the implementer's worktree.

.PARAMETER Base
Commit the worktree started from. The worktree HEAD must equal it.

.PARAMETER Target
Integration checkout. Defaults to the repo root, resolved from this script's location.
#>
param(
    [Parameter(Mandatory)][string]$Worktree,
    [Parameter(Mandatory)][string]$Base,
    [string]$Target = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

function Stop-Script {
    param([int]$Code, [string]$Message)
    [Console]::Error.WriteLine($Message)
    exit $Code
}

function Invoke-Git {
    param([string]$Repo, [string[]]$GitArgs)
    $out = & git -C $Repo -c core.quotepath=off @GitArgs
    if ($LASTEXITCODE -ne 0) {
        Stop-Script 2 "git $($GitArgs -join ' ') failed in $Repo (exit $LASTEXITCODE)"
    }
    return @($out | Where-Object { $_ -ne '' })
}

foreach ($p in @($Worktree, $Target)) {
    if (-not (Test-Path -LiteralPath $p -PathType Container)) {
        Stop-Script 2 "Not a directory: $p"
    }
}
$Worktree = (Resolve-Path -LiteralPath $Worktree).Path
$Target = (Resolve-Path -LiteralPath $Target).Path
if ($Worktree -eq $Target) { Stop-Script 2 'Worktree and Target are the same directory.' }

# Pre-checks: Target clean, worktree HEAD equals Base.
$dirty = Invoke-Git $Target @('status', '--porcelain')
if ($dirty.Count -gt 0) {
    Stop-Script 2 "Target working tree is not clean:`n$($dirty -join "`n")"
}
$baseSha = @(Invoke-Git $Worktree @('rev-parse', '--verify', "$Base^{commit}"))[0]
$headSha = @(Invoke-Git $Worktree @('rev-parse', 'HEAD'))[0]
if ($baseSha -ne $headSha) {
    Stop-Script 2 "Worktree HEAD $headSha does not equal Base $baseSha."
}

# Collect what to carry over.
$tracked = Invoke-Git $Worktree @('diff', '--name-status', '--no-renames', $baseSha)
$untracked = Invoke-Git $Worktree @('ls-files', '--others', '--exclude-standard')

# Refuse before touching anything when an untracked file would overwrite a Target file.
$clashes = $untracked | Where-Object { Test-Path -LiteralPath (Join-Path $Target $_) }
if ($clashes) {
    Stop-Script 2 "Untracked files already exist in Target:`n$($clashes -join "`n")"
}

# Tracked changes: raw bytes via cmd redirection, since PowerShell pipes re-encode text.
$patch = Join-Path ([IO.Path]::GetTempPath()) "integrate-$([guid]::NewGuid().ToString('N')).patch"
try {
    & cmd /c "git -C `"$Worktree`" diff --binary $baseSha > `"$patch`""
    if ($LASTEXITCODE -ne 0) { Stop-Script 2 "git diff failed in $Worktree (exit $LASTEXITCODE)" }

    if ((Get-Item -LiteralPath $patch).Length -gt 0) {
        & git -C $Target apply --3way $patch
        $applyExit = $LASTEXITCODE
        $conflicted = @(& git -C $Target diff --name-only --diff-filter=U)
        if ($conflicted.Count -gt 0) {
            Write-Host 'CONFLICTS (left for a merger):'
            $conflicted | ForEach-Object { Write-Host "  $_" }
            exit 3
        }
        if ($applyExit -ne 0) {
            Stop-Script 4 "git apply --3way failed without conflicts (exit $applyExit); Target unchanged."
        }
    }
}
finally {
    Remove-Item -LiteralPath $patch -Force -ErrorAction SilentlyContinue
}

# Untracked files: plain copy, left unstaged.
foreach ($rel in $untracked) {
    $dest = Join-Path $Target $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath (Join-Path $Worktree $rel) -Destination $dest
}

& pwsh -NoProfile -File (Join-Path $Target 'scripts/gate.ps1')
$gateExit = $LASTEXITCODE

Write-Host ''
Write-Host 'Changed files (status path):'
$tracked | ForEach-Object { Write-Host "  $($_ -replace '\t', ' ')" }
Write-Host 'New files:'
$untracked | ForEach-Object { Write-Host "  $_" }
Write-Host "Gate exit code: $gateExit. The caller stages the paths above by name and commits."
exit $gateExit
