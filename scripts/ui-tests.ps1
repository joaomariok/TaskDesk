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
# It keeps the display awake while it runs, so the idle timeout cannot lock the session mid-run, and refuses to start
# on a locked desktop. Each failed test is rerun once: a pass prints FLAKE: <test>, a second failure FAILED: <test>.
# Exit codes: 0 all passed first time; 5 only flakes (every failure passed on rerun); 6 the desktop is locked;
# any other value is the test run's own exit code.
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

Add-Type -Namespace UiTests -Name Desktop -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
[DllImport("user32.dll")]
static extern bool CloseDesktop(IntPtr desktop);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern bool GetUserObjectInformation(IntPtr obj, int index, System.Text.StringBuilder info, int length, out int needed);
[DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint flags);

// The input desktop is "Default" while the user's session takes input; a locked session switches it to
// Winlogon, which a normal process cannot open.
public static bool IsLocked() {
    IntPtr desktop = OpenInputDesktop(0, false, 0x0001);
    if (desktop == IntPtr.Zero) return true;
    try {
        var name = new System.Text.StringBuilder(256);
        int needed;
        if (!GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed)) return true;
        return name.ToString() != "Default";
    }
    finally { CloseDesktop(desktop); }
}
'@

# ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED, held until reset to ES_CONTINUOUS.
[void][UiTests.Desktop]::SetThreadExecutionState(0x80000003u)

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

    # Every test would fail on "Another window covers TaskDesk's title bar"; stop before the run instead.
    if ([UiTests.Desktop]::IsLocked()) {
        Write-Host 'UI TESTS NOT RUN: the desktop is locked; unlock it and rerun.'
        exit 6
    }

    $runArgs = @('run', '--project', 'tests/TaskDesk.UiTests', '--no-build', '--')
    if ($Class) { $runArgs += @('-class', "TaskDesk.UiTests.$Class") }
    if ($Method) { $runArgs += @('-method', ($Method.Contains('.') ? $Method : "*.$Method")) }
    & dotnet @runArgs | Tee-Object -Variable runOutput
    $code = $LASTEXITCODE
    # "    TaskDesk.UiTests.Class.Method [FAIL]"; a theory's arguments follow the method name in parentheses.
    $failedTests = @($runOutput | ForEach-Object { if ("$_" -match '^\s*(TaskDesk\.UiTests\.[\w.]+).*\[FAIL\]') { $Matches[1] } } |
        Select-Object -Unique)
    $lostFocus = @($runOutput | Where-Object { "$_" -match 'did not take focus' }).Count
    if ($failedTests.Count -gt 0 -and $lostFocus -gt $failedTests.Count / 2) {
        Write-Host 'Many tests lost keyboard focus: another window took the foreground, or keys were pressed during the run.'
    }

    $real = @()
    if ($code -ne 0 -and $failedTests.Count -gt 0) {
        Write-Host "Rerunning $($failedTests.Count) failed test(s) once."
        foreach ($test in $failedTests) {
            & dotnet run --project tests/TaskDesk.UiTests --no-build -- -method $test | Out-Host
            if ($LASTEXITCODE -ne 0) { $real += $test }
        }
        foreach ($test in $failedTests) {
            Write-Host (($real -contains $test) ? "FAILED: $test" : "FLAKE: $test")
        }
        if ($real.Count -eq 0) { $code = 5 }
    }
    if (Test-Path $failures) { Write-Host "Failure screenshots: $failures" }
    Write-Host ($code -eq 0 ? 'UI TESTS PASSED' : ($code -eq 5 ? 'UI TESTS PASSED WITH FLAKES: exit 5' : "UI TESTS FAILED: exit $code"))
    exit $code
}
finally {
    [void][UiTests.Desktop]::SetThreadExecutionState(0x80000000u)
    Remove-Item Env:TASKDESK_UI_APP -ErrorAction SilentlyContinue
    Pop-Location
}
