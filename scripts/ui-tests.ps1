#requires -Version 7
# Opt-in UI test suite: drives the real TaskDesk window through Windows UI Automation (tests/TaskDesk.UiTests).
# Needs an interactive desktop; it takes over the keyboard and mouse while it runs (about a minute).
# Each test uses a fresh temp data folder (sandbox mode: no Run key, no notification registration).
#   -Publish <dir>  test TaskDesk.App.exe in a publish folder instead of the Debug build
#   -Class <name>   run one test class, for example NavigationTests
param(
    [string]$Publish,
    [string]$Class
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    if ($Publish) {
        $exe = Join-Path (Resolve-Path $Publish) 'TaskDesk.App.exe'
        if (-not (Test-Path $exe)) { throw "No TaskDesk.App.exe in $Publish" }
        $env:TASKDESK_UI_APP = $exe
    }
    else {
        Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
        & dotnet build src/TaskDesk.App -warnaserror
        if ($LASTEXITCODE -ne 0) { Write-Host "UI TESTS FAILED: app build exit $LASTEXITCODE"; exit $LASTEXITCODE }
    }

    & dotnet build tests/TaskDesk.UiTests -warnaserror
    if ($LASTEXITCODE -ne 0) { Write-Host "UI TESTS FAILED: test build exit $LASTEXITCODE"; exit $LASTEXITCODE }

    $runArgs = @('run', '--project', 'tests/TaskDesk.UiTests', '--no-build', '--')
    if ($Class) { $runArgs += @('-class', "TaskDesk.UiTests.$Class") }
    & dotnet @runArgs
    $code = $LASTEXITCODE
    Write-Host ($code -eq 0 ? 'UI TESTS PASSED' : "UI TESTS FAILED: exit $code")
    exit $code
}
finally {
    Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
    Pop-Location
}
