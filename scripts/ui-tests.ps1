#requires -Version 7
# Opt-in UI test suite: drives the real TaskDesk window through Windows UI Automation (tests/TaskDesk.UiTests).
# Needs an interactive desktop; it takes over the keyboard and mouse while it runs (about a minute).
# Each test uses a fresh temp data folder (sandbox mode: no Run key, no notification registration).
#   -Publish <dir>  test TaskDesk.App.exe in a publish folder instead of the Debug build
#   -Class <name>   run one test class, for example NavigationTests
#   -Method <name>  run the tests whose method name matches, with * as a wildcard, for example *Ctrl_comma*
# Before the run it stops TaskDesk.App processes left over from earlier UI runs
# (identified by running from this repo's bin folder, or from -Publish; the TASKDESK_DATA_DIR environment cannot be read
# from another process) and warns about any other running one without touching it.
param(
    [string]$Publish,
    [string]$Class,
    [string]$Method
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appName = 'TaskDesk.App'
$appProject = "src/$appName"

function Get-TaskDeskProcess {
    Get-Process -Name $appName -ErrorAction SilentlyContinue | Where-Object { $_.Path }
}

Push-Location $repoRoot
try {
    if ($Publish) {
        $exe = Join-Path (Resolve-Path $Publish) "$appName.exe"
        if (-not (Test-Path $exe)) { throw "No $appName.exe in $Publish" }
        $env:TASKDESK_UI_APP = $exe
    }
    else {
        Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
        & dotnet build $appProject -warnaserror
        if ($LASTEXITCODE -ne 0) { Write-Host "UI TESTS FAILED: app build exit $LASTEXITCODE"; exit $LASTEXITCODE }
        # AppSession finds the newest Debug build by itself.
    }

    # Leftovers from an earlier UI run would hold focus and the keyboard; the owner's own instance is left alone.
    $ownRoots = @(Join-Path $repoRoot "src\$appName\bin")
    if ($Publish) { $ownRoots += (Resolve-Path $Publish).Path }
    foreach ($process in Get-TaskDeskProcess) {
        $isLeftover = $ownRoots | Where-Object { $process.Path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }
        if ($isLeftover) {
            Write-Host "Stopping leftover $appName (pid $($process.Id)): $($process.Path)"
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        else {
            Write-Warning "$appName is running (pid $($process.Id), $($process.Path)); not stopped. It may take focus from the tests."
        }
    }

    # A failed test leaves a PNG of the window here; clear the last run's so none is stale.
    $failures = Join-Path $repoRoot 'TestResults/ui-failures'
    Remove-Item $failures -Recurse -Force -ErrorAction SilentlyContinue

    & dotnet build tests/TaskDesk.UiTests -warnaserror
    if ($LASTEXITCODE -ne 0) { Write-Host "UI TESTS FAILED: test build exit $LASTEXITCODE"; exit $LASTEXITCODE }

    $runArgs = @('run', '--project', 'tests/TaskDesk.UiTests', '--no-build', '--')
    if ($Class) { $runArgs += @('-class', "TaskDesk.UiTests.$Class") }
    if ($Method) { $runArgs += @('-method', ($Method.Contains('.') ? $Method : "*.$Method")) }
    & dotnet @runArgs | Tee-Object -Variable runOutput
    $code = $LASTEXITCODE
    $failed = @($runOutput | Where-Object { "$_" -match '\[FAIL\]' }).Count
    $lostFocus = @($runOutput | Where-Object { "$_" -match 'did not take focus' }).Count
    if ($failed -gt 0 -and $lostFocus -gt $failed / 2) {
        Write-Host 'Many tests lost keyboard focus: another window took the foreground, or keys were pressed during the run.'
    }
    if (Test-Path $failures) { Write-Host "Failure screenshots: $failures" }
    Write-Host ($code -eq 0 ? 'UI TESTS PASSED' : "UI TESTS FAILED: exit $code")
    exit $code
}
finally {
    Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
    Pop-Location
}
