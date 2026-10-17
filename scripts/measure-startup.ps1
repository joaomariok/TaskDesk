#requires -Version 7
<#
.SYNOPSIS
Measures cold start to a visible window and the --minimized working set of a TaskDesk publish.

.DESCRIPTION
Creates a fresh data folder under $env:TEMP\TaskDesk-measure, fills it with sample Tasks and always sets
TASKDESK_DATA_DIR (sandbox mode stays on; TASKDESK_SANDBOX is never set). One discarded warm-up launch, then
-Runs timed launches measured from just before start until the main window is visible (5 ms poll). Then 3
launches with --minimized, reading WorkingSet64 after 12 s. Every started process is killed, also on Ctrl+C
or error. Refuses to start while a TaskDesk.App process is running.

.EXAMPLE
pwsh scripts/measure-startup.ps1 -Publish C:\pub\win-x64-fdd -Label fdd
#>
param(
    [Parameter(Mandatory)][string]$Publish,
    [int]$Runs = 7,
    [int]$Tasks = 5000,
    [string]$Label = 'run'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $Publish 'TaskDesk.App.exe'
$processName = 'TaskDesk.App'
if (-not (Test-Path -LiteralPath $exe)) { throw "No TaskDesk.App.exe in '$Publish'." }

Add-Type -Namespace TaskDeskMeasure -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);'

function Assert-NotRunning {
    $running = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($running) { throw "$processName is already running (PID $($running.Id -join ', ')); close it first." }
}

function Stop-Started($p) {
    if ($p -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        $p.WaitForExit(10000) | Out-Null
    }
}

function Get-Median($xs) {
    if (-not $xs -or @($xs).Count -eq 0) { return [double]::NaN }
    $s = @($xs | Sort-Object)
    if ($s.Count % 2) { return $s[[int][math]::Floor($s.Count / 2)] }
    return ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2
}

Assert-NotRunning

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dataDir = Join-Path $env:TEMP "TaskDesk-measure\$Label-$stamp"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Write-Host "Data folder: $dataDir"

Push-Location $repoRoot
try {
    & dotnet run --project tools/TaskDesk.SampleData -- $dataDir $Tasks
    if ($LASTEXITCODE -ne 0) { throw "Sample data tool failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$savedData = $env:TASKDESK_DATA_DIR
$savedSandbox = $env:TASKDESK_SANDBOX
$env:TASKDESK_DATA_DIR = $dataDir
Remove-Item Env:TASKDESK_SANDBOX -ErrorAction SilentlyContinue

$cold = [System.Collections.Generic.List[double]]::new()
$ws = [System.Collections.Generic.List[double]]::new()
$p = $null
try {
    for ($i = 0; $i -le $Runs; $i++) {
        Assert-NotRunning
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $exe -PassThru
        $visible = $false
        while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $p.HasExited) {
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero -and
                [TaskDeskMeasure.Native]::IsWindowVisible($p.MainWindowHandle)) {
                $visible = $true
                break
            }
            Start-Sleep -Milliseconds 5
        }
        $ms = $sw.Elapsed.TotalMilliseconds
        Start-Sleep -Milliseconds 1500
        Stop-Started $p
        $p = $null
        if (-not $visible) { Write-Host "cold run $i : no visible window"; continue }
        $note = if ($i -eq 0) { ' (warm-up, discarded)' } else { '' }
        Write-Host "cold run $i : $([math]::Round($ms)) ms$note"
        if ($i -gt 0) { $cold.Add($ms) }
        Start-Sleep -Milliseconds 1000
    }

    for ($i = 1; $i -le 3; $i++) {
        Assert-NotRunning
        $p = Start-Process -FilePath $exe -ArgumentList '--minimized' -PassThru
        Start-Sleep -Seconds 12
        $p.Refresh()
        if ($p.HasExited) {
            Write-Host "ws run $i : exited with $($p.ExitCode)"
            $p = $null
            continue
        }
        $mb = $p.WorkingSet64 / 1MB
        Write-Host "ws run $i : working set $([math]::Round($mb, 1)) MB"
        $ws.Add($mb)
        Stop-Started $p
        $p = $null
        Start-Sleep -Milliseconds 1000
    }
}
finally {
    Stop-Started $p
    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Resolve-Path -LiteralPath $exe).Path } |
        ForEach-Object { Stop-Started $_ }
    $env:TASKDESK_DATA_DIR = $savedData
    if ($null -ne $savedSandbox) { $env:TASKDESK_SANDBOX = $savedSandbox }
}

if ($cold.Count -eq 0 -or $ws.Count -eq 0) { throw 'No successful runs; cannot report a result.' }
$min = [math]::Round(($cold | Measure-Object -Minimum).Minimum)
$max = [math]::Round(($cold | Measure-Object -Maximum).Maximum)
$coldMedian = [math]::Round((Get-Median $cold))
$wsMedian = [math]::Round((Get-Median $ws), 1)
Write-Host "RESULT ${Label}: cold median $coldMedian ms ($min..$max), working set median $wsMedian MB"
