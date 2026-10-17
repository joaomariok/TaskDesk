#requires -Version 7
# Verification gate: build-server shutdown, warnings-as-errors build, tests.
# Stops at the first non-zero exit code and exits with it.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
    param([string]$Name, [string[]]$DotnetArgs)
    Write-Host "== dotnet $($DotnetArgs -join ' ')"
    $lines = [System.Collections.Generic.List[string]]::new()
    & dotnet @DotnetArgs 2>&1 | ForEach-Object {
        $line = "$_"
        $lines.Add($line)
        Write-Host $line
    }
    return [pscustomobject]@{ Name = $Name; Code = $LASTEXITCODE; Output = $lines }
}

function Get-TestTotal {
    param([System.Collections.Generic.List[string]]$Output, [string]$Label)
    $values = foreach ($l in $Output) {
        if ($l -match "^\s*$Label\s*:\s*(\d+)\s*$") { [int]$Matches[1] }
    }
    return [int](($values | Measure-Object -Sum).Sum)
}

function Test-OnlyLockErrors {
    param([System.Collections.Generic.List[string]]$Output)
    $errors = @($Output | Where-Object { $_ -match ':\s*error\s+[A-Z]+\d+' })
    if ($errors.Count -eq 0) { return $false }
    $nonLock = @($errors | Where-Object { $_ -notmatch ':\s*error\s+MSB30(21|26|27)\b' })
    return $nonLock.Count -eq 0
}

Push-Location $repoRoot
try {
    $shutdown = Invoke-Step 'build-server shutdown' @('build-server', 'shutdown')
    if ($shutdown.Code -ne 0) {
        Write-Host "GATE FAILED: build-server shutdown exit $($shutdown.Code)"
        exit $shutdown.Code
    }

    $build = Invoke-Step 'build' @('build', '-warnaserror')
    $retried = $false
    if ($build.Code -ne 0 -and (Test-OnlyLockErrors $build.Output)) {
        # Another process (often the editor's background build) held an output file. Retry once.
        Write-Host '== build failed only on locked output files; retrying once in 5 s'
        Start-Sleep -Seconds 5
        $build = Invoke-Step 'build' @('build', '-warnaserror')
        $retried = $true
    }
    if ($build.Code -ne 0) {
        Write-Host "GATE FAILED: build exit $($build.Code), tests not run"
        exit $build.Code
    }

    $test = Invoke-Step 'test' @('test')
    # The UI suite takes over the desktop and must stay opt-in (IsTestingPlatformApplication=false in its csproj).
    if ($test.Output | Where-Object { $_ -match 'TaskDesk\.UiTests' }) {
        Write-Host 'GATE FAILED: dotnet test ran TaskDesk.UiTests; keep it out of the gate (run scripts/ui-tests.ps1)'
        exit 1
    }
    $total = Get-TestTotal $test.Output 'total'
    $failed = Get-TestTotal $test.Output 'failed'
    $passed = Get-TestTotal $test.Output 'succeeded'
    $skipped = Get-TestTotal $test.Output 'skipped'
    $summary = "build exit $($build.Code), test exit $($test.Code); " +
        "tests total $total, passed $passed, failed $failed, skipped $skipped"
    if ($test.Code -ne 0) {
        Write-Host "GATE FAILED: $summary"
        exit $test.Code
    }
    if ($retried) { $summary += ' (after lock retry)' }
    Write-Host "GATE PASSED: $summary"
    exit 0
}
finally {
    Pop-Location
}
