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

Push-Location $repoRoot
try {
    $shutdown = Invoke-Step 'build-server shutdown' @('build-server', 'shutdown')
    if ($shutdown.Code -ne 0) {
        Write-Host "GATE FAILED: build-server shutdown exit $($shutdown.Code)"
        exit $shutdown.Code
    }

    $build = Invoke-Step 'build' @('build', '-warnaserror')
    if ($build.Code -ne 0) {
        Write-Host "GATE FAILED: build exit $($build.Code), tests not run"
        exit $build.Code
    }

    $test = Invoke-Step 'test' @('test')
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
    Write-Host "GATE PASSED: $summary"
    exit 0
}
finally {
    Pop-Location
}
