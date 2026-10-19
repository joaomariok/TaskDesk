#requires -Version 7
# Starts TaskDesk on a throwaway data folder, for looking at it with `winapp ui` (the ui-check skill). The folder
# turns on sandbox mode, so the owner's data, the Run key and notification registration are never touched.
#   -Theme <name>        Light, Dark or System (default), written to the folder's settings.json
#   -Width/-Height <px>  the main window's size (the app's minimum is 800 x 500; below 900 wide the sidebar is
#                        the icon rail); unset, the app opens at its default 900 x 600
#   -SampleTasks <n>     fill the folder with n varied Tasks first (tools/TaskDesk.SampleData); 0 starts empty
#   -Ref <git ref>       run that commit instead of this checkout, for a before/after look (for example main). It is
#                        checked out in a worktree under %TEMP%\TaskDesk-ref-<ref> and built there; the worktree
#                        stays for the next run (remove it with `git worktree remove`). A ref from before the
#                        WinUI swap (#68) runs the WPF app, which has the same project path and exe name.
# Closing the window exits the app. It prints the process id for `winapp ui ... -a <pid>` and the data folder.
# Each run first deletes the sandbox folders of earlier runs whose app has exited.
param(
    [ValidateSet('Light', 'Dark', 'System')][string]$Theme = 'System',
    [int]$Width,
    [int]$Height,
    [ValidateRange(0, 100000)][int]$SampleTasks = 0,
    [string]$Ref
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appName = 'TaskDesk.App'

# Earlier sandboxes: each holds the id of the app that used it.
foreach ($old in Get-ChildItem $env:TEMP -Directory -Filter 'TaskDesk-sandbox-*') {
    $pidFile = Join-Path $old.FullName 'sandbox.pid'
    $running = (Test-Path $pidFile) -and (Get-Process -Id (Get-Content $pidFile) -ErrorAction SilentlyContinue)
    if (-not $running) { Remove-Item $old.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}

$root = $repoRoot
if ($Ref) {
    $commit = (& git -C $repoRoot rev-parse --verify --quiet "$Ref^{commit}")
    if (-not $commit) { throw "Unknown git ref: $Ref" }
    $root = Join-Path $env:TEMP ('TaskDesk-ref-' + ($Ref -replace '[^A-Za-z0-9._-]', '-'))
    if (Test-Path (Join-Path $root '.git')) {
        & git -C $root checkout --quiet --detach --force $commit
    }
    else {
        & git -C $repoRoot worktree add --quiet --detach --force $root $commit
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not check out $Ref in $root" }
    Write-Host "Running $Ref ($($commit.Substring(0, 9))) from $root"
}

$appProject = Join-Path $root "src/$appName"
if (-not (Test-Path $appProject)) { throw "No src/$appName in $root" }
# A TaskDesk running from this build's output (a debug session, another sandbox) locks it once there is something to
# copy. The build then fails at once (no copy retries) and only the holder is reported, not every retry warning.
$build = & dotnet build $appProject -nologo -v q -p:CopyRetryCount=0 2>&1
if ($LASTEXITCODE -ne 0) {
    $binDir = [IO.Path]::GetFullPath((Join-Path $appProject 'bin')) + [IO.Path]::DirectorySeparatorChar
    $holder = Get-Process -Name $appName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($binDir, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($holder -and ($build -match 'error MSB30(21|26|27)')) {
        throw "$appName (pid $($holder.Id)) runs from $($holder.Path) and locks the build output; close it (or Stop-Process $($holder.Id)) and run again."
    }
    $build | Write-Host
    throw "Build failed in $root"
}
$exe = Get-ChildItem (Join-Path $appProject 'bin/Debug') -Filter "$appName.exe" -Recurse |
    Sort-Object LastWriteTimeUtc | Select-Object -Last 1
if (-not $exe) { throw "No $appName.exe under $appProject\bin\Debug" }

$data = Join-Path $env:TEMP ('TaskDesk-sandbox-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $data | Out-Null
# This script's id until the app's replaces it, so a run started meanwhile does not take the folder for a leftover.
Set-Content (Join-Path $data 'sandbox.pid') $PID
# As in the UI tests: closing the window exits, no first-close hint, no Run key, no global hotkey.
$settings = @{
    closeBehavior = 'Exit'; trayHintShown = $true; startWithWindows = $false; globalHotkey = ''; themeMode = $Theme
}
if ($Width -or $Height) {
    $settings.windowLayout = @{ left = 80; top = 80; width = ($Width ? $Width : 900); height = ($Height ? $Height : 600) }
}
Set-Content (Join-Path $data 'settings.json') -Encoding utf8 -Value ($settings | ConvertTo-Json)

if ($SampleTasks -gt 0) {
    & dotnet run --project (Join-Path $root 'tools/TaskDesk.SampleData') -v q -- $data $SampleTasks
    if ($LASTEXITCODE -ne 0) { throw 'The sample data tool failed.' }
}

# Through the shell, so the app does not inherit this script's output handles: a caller that waits for those to
# close (an agent's shell tool) would otherwise wait until the app exits. The child takes this process's
# environment, so the data folder is set for the launch only.
$saved = @{ Data = $env:TASKDESK_DATA_DIR; Sandbox = $env:TASKDESK_SANDBOX }
try {
    $env:TASKDESK_DATA_DIR = $data
    Remove-Item Env:TASKDESK_SANDBOX -ErrorAction SilentlyContinue
    $app = Start-Process $exe.FullName -PassThru
}
finally {
    $env:TASKDESK_DATA_DIR = $saved.Data
    $env:TASKDESK_SANDBOX = $saved.Sandbox
}
Set-Content (Join-Path $data 'sandbox.pid') $app.Id

$deadline = (Get-Date).AddSeconds(20)
while ($app.MainWindowHandle -eq 0 -and -not $app.HasExited -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
    $app.Refresh()
}
if ($app.HasExited) { throw "TaskDesk exited at start-up (code $($app.ExitCode)); see the log in $data" }
if ($app.MainWindowHandle -eq 0) { throw "TaskDesk showed no window within 20 s (pid $($app.Id))." }

Write-Host "$appName pid $($app.Id), theme $Theme, data $data"
Write-Host "Look with: winapp ui inspect -a $($app.Id)   Close the window (or Stop-Process $($app.Id)) when done."
