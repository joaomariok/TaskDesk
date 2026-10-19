#requires -Version 7
<#
.SYNOPSIS
Owner-run check that a real Windows app notification (toast) appears for a due Reminder.

.DESCRIPTION
OWNER-RUN: this is not for agents. The final launch runs with TASKDESK_SANDBOX=0, so TaskDesk registers a
notification AppUserModelId (plus COM class and icon file) for that exe path under HKCU. A run that is not a
Velopack install removes that registration when it exits; one that is killed or crashes leaves it behind.

Steps: refuse if TaskDesk is running; create a fresh temp data folder with startWithWindows=false in
settings.json; launch once in sandbox mode to create the database, then stop it; seed one Task in the Default
List with a Reminder 5 s in the past (sqlite3); relaunch with TASKDESK_SANDBOX=0 and leave it running.
-DryRun stops before that final launch.

.EXAMPLE
pwsh scripts/verify-toast.ps1 -Publish C:\pub\win-x64
#>
param(
    [Parameter(Mandatory)][string]$Publish,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $Publish 'TaskDesk.App.exe'
$processName = 'TaskDesk.App'
if (-not (Test-Path -LiteralPath $exe)) { throw "No TaskDesk.App.exe in '$Publish'." }
if (-not (Get-Command sqlite3 -ErrorAction SilentlyContinue)) { throw 'sqlite3 was not found on PATH.' }
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    throw 'TaskDesk is running; close it first (including the tray icon).'
}

$dataDir = Join-Path $env:TEMP "TaskDesk-toast\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Set-Content -Path (Join-Path $dataDir 'settings.json') -Value '{"startWithWindows":false}' -NoNewline
$db = Join-Path $dataDir 'taskdesk.db'
Write-Host "Data folder: $dataDir"

function Start-App([string]$sandbox, [string[]]$AppArgs = @()) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
    foreach ($a in $AppArgs) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.Environment['TASKDESK_DATA_DIR'] = $dataDir
    $psi.Environment['TASKDESK_SANDBOX'] = $sandbox
    return [System.Diagnostics.Process]::Start($psi)
}

# Step 1: sandbox launch to create the database.
$p = Start-App '1' @('--minimized')
try {
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $db) -and (Get-Date) -lt $deadline -and -not $p.HasExited) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $db)) { throw "The database was not created at $db." }
    Start-Sleep -Seconds 2   # let migrations and seeding finish
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(10000) | Out-Null }
}
Write-Host 'Database created in sandbox mode; app stopped.'

# Step 2: seed one due Reminder in the Default List.
$listId = (sqlite3 $db 'select Id from Lists where IsDefault = 1;' | Out-String).Trim()
if (-not $listId) { throw 'No Default List found in the database.' }
$id = [guid]::NewGuid().ToString().ToUpperInvariant()
$now = [DateTime]::UtcNow
$fmt = 'yyyy-MM-dd HH:mm:ss.fffffff'
$created = $now.ToString($fmt)
$reminder = $now.AddSeconds(-5).ToString($fmt)
sqlite3 $db "insert into Tasks (Id, ListId, Title, IsCompleted, SortOrder, CreatedUtc, UpdatedUtc, Priority, ReminderUtc) values ('$id', '$listId', 'Toast test', 0, 1000, '$created', '$created', 0, '$reminder');"
if ($LASTEXITCODE -ne 0) { throw "sqlite3 insert failed with exit code $LASTEXITCODE." }
Write-Host "Seeded Task $id with a Reminder due at $reminder UTC"

if ($DryRun) {
    Write-Host 'DryRun: stopping before the sandbox-off launch.'
    return
}

# Step 3: real launch (sandbox off) so the notification registers.
$app = Start-App '0'
Write-Host "Started TaskDesk (pid $($app.Id)) with TASKDESK_SANDBOX=0."
Write-Host @'

Look for:
  - A Windows toast titled "Toast test" within a few seconds.
  - Clicking the toast body or a button brings TaskDesk forward / acts on the Task.
  - The startup log in the data folder's logs directory says "App notifications registered".
  - No "balloon" fallback banner in the main window.
When done, choose Exit TaskDesk from the tray icon. The app is left running for you.
'@
