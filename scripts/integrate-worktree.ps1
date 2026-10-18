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

When the apply leaves conflicts, the untracked files are still copied (and listed). The one
recurring trivial conflict is resolved automatically: in src/TaskDesk.App/App.xaml, hunks where
both sides only add <ResourceDictionary Source="..."/> lines become ours then theirs, with no
markers left (the index is not touched). Any other conflict exits 3.

With -UiTests, after the gate passes it runs scripts/ui-tests.ps1 in the target and exits with
that script's code. It needs an interactive desktop.

Exit codes:
  0   gate passed (and the UI tests too, with -UiTests)
  2   pre-check failed (nothing was changed)
  3   apply left conflicts (conflicted files listed; resolution is left to a merger)
  4   apply failed without conflicts (nothing was changed)
  any other value is the gate's own exit code (or ui-tests.ps1's, with -UiTests: 5 only flakes,
  6 desktop locked)

.PARAMETER Worktree
Path of the implementer's worktree.

.PARAMETER Base
Commit the worktree started from. The worktree HEAD must equal it.

.PARAMETER Target
Integration checkout. Defaults to the repo root, resolved from this script's location.

.PARAMETER UiTests
After a passing gate, run scripts/ui-tests.ps1 and exit with its code.
#>
param(
    [Parameter(Mandatory)][string]$Worktree,
    [Parameter(Mandatory)][string]$Base,
    [string]$Target = (Split-Path -Parent $PSScriptRoot),
    [switch]$UiTests
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

# Resolves hunks in which both sides only add <ResourceDictionary Source="..."/> lines: ours, then
# theirs (a line both sides added is kept once). Returns $true when the file is left marker-free.
function Resolve-DictionaryConflicts {
    param([string]$Path)
    $enc = [Text.UTF8Encoding]::new($false)
    $lines = $enc.GetString([IO.File]::ReadAllBytes($Path)) -split "`n"
    $dict = '^\s*<ResourceDictionary\s+Source="[^"]*"\s*/>\s*$'
    $out = [Collections.Generic.List[string]]::new()
    $i = 0
    while ($i -lt $lines.Count) {
        if ($lines[$i] -notmatch '^<<<<<<< ') { $out.Add($lines[$i]); $i++; continue }
        $ours = @(); $theirs = @(); $side = 'ours'; $i++
        while ($i -lt $lines.Count -and $lines[$i] -notmatch '^>>>>>>>') {
            if ($lines[$i] -match '^=======\s*$') { $side = 'theirs' }
            elseif ($lines[$i] -match '^\|\|\|\|\|\|\|') { return $false }
            elseif ($side -eq 'ours') { $ours += $lines[$i] }
            else { $theirs += $lines[$i] }
            $i++
        }
        if ($i -ge $lines.Count) { return $false }
        $i++
        $all = @($ours) + @($theirs)
        if (@($all | Where-Object { $_ -notmatch $dict }).Count -gt 0) { return $false }
        $seen = @{}
        foreach ($l in $all) {
            $key = $l.Trim()
            if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; $out.Add($l) }
        }
    }
    [IO.File]::WriteAllBytes($Path, $enc.GetBytes(($out -join "`n")))
    return $true
}

# Untracked files: plain copy, left unstaged.
function Copy-Untracked {
    foreach ($rel in $untracked) {
        $dest = Join-Path $Target $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -LiteralPath (Join-Path $Worktree $rel) -Destination $dest
    }
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
        $autoResolved = $false
        if ($conflicted -contains 'src/TaskDesk.App/App.xaml') {
            if (Resolve-DictionaryConflicts (Join-Path $Target 'src/TaskDesk.App/App.xaml')) {
                Write-Host 'Auto-resolved src/TaskDesk.App/App.xaml (merged-dictionary lines: ours, then theirs; not staged).'
                $autoResolved = $true
                $conflicted = @($conflicted | Where-Object { $_ -ne 'src/TaskDesk.App/App.xaml' })
            }
        }
        if ($conflicted.Count -gt 0) {
            Copy-Untracked
            if ($untracked.Count -gt 0) {
                Write-Host 'Untracked files copied despite the conflicts:'
                $untracked | ForEach-Object { Write-Host "  $_" }
            }
            Write-Host 'CONFLICTS (left for a merger):'
            $conflicted | ForEach-Object { Write-Host "  $_" }
            exit 3
        }
        if ($applyExit -ne 0 -and -not $autoResolved) {
            Stop-Script 4 "git apply --3way failed without conflicts (exit $applyExit); Target unchanged."
        }
    }
}
finally {
    Remove-Item -LiteralPath $patch -Force -ErrorAction SilentlyContinue
}

Copy-Untracked

& pwsh -NoProfile -File (Join-Path $Target 'scripts/gate.ps1')
$gateExit = $LASTEXITCODE

Write-Host ''
Write-Host 'Changed files (status path):'
$tracked | ForEach-Object { Write-Host "  $($_ -replace '\t', ' ')" }
Write-Host 'New files:'
$untracked | ForEach-Object { Write-Host "  $_" }
Write-Host "Gate exit code: $gateExit. The caller stages the paths above by name and commits."
if ($gateExit -ne 0 -or -not $UiTests) { exit $gateExit }

$uiArgs = @('-NoProfile', '-File', (Join-Path $Target 'scripts/ui-tests.ps1'))
& pwsh @uiArgs
$uiExit = $LASTEXITCODE
Write-Host "UI tests exit code: $uiExit."
exit $uiExit
