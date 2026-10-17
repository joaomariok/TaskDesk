#requires -Version 7
# Opt-in UI test suite: drives the real TaskDesk window through Windows UI Automation (tests/TaskDesk.UiTests).
# Needs an interactive desktop; it takes over the keyboard and mouse while it runs (about a minute).
# Each test uses a fresh temp data folder (sandbox mode: no Run key, no notification registration).
#   -Publish <dir>  test TaskDesk.App.exe in a publish folder instead of the Debug build
#   -Class <name>   run one test class, for example NavigationTests
#   -Screenshots <dir>  save PNGs of the ScreenshotTour states (Light and Dark) into this folder
param(
    [string]$Publish,
    [string]$Class,
    [string]$Screenshots
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

    if ($Screenshots) {
        $env:TASKDESK_UI_SCREENSHOTS = (New-Item -ItemType Directory -Force $Screenshots).FullName
    }

    & dotnet build tests/TaskDesk.UiTests -warnaserror
    if ($LASTEXITCODE -ne 0) { Write-Host "UI TESTS FAILED: test build exit $LASTEXITCODE"; exit $LASTEXITCODE }

    $runArgs = @('run', '--project', 'tests/TaskDesk.UiTests', '--no-build', '--')
    if ($Class) { $runArgs += @('-class', "TaskDesk.UiTests.$Class") }
    & dotnet @runArgs
    $code = $LASTEXITCODE
    if ($Screenshots) { Write-Host "Screenshots: $env:TASKDESK_UI_SCREENSHOTS" }
    Write-Host ($code -eq 0 ? 'UI TESTS PASSED' : "UI TESTS FAILED: exit $code")
    exit $code
}
finally {
    Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
    Remove-Item Env:TASKDESK_UI_SCREENSHOTS -ErrorAction SilentlyContinue
    Pop-Location
}
