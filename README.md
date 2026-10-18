# TaskDesk

A small, fast, local-only to-do list for Windows 11. It lives in the system tray, starts with Windows and shows Windows notifications for Reminders. No network calls, no telemetry.

Product spec: [docs/SPEC.md](docs/SPEC.md). Vocabulary: [GLOSSARY.md](GLOSSARY.md). Decisions: [docs/adr/](docs/adr/). What to check by hand: [docs/MANUAL-TESTS.md](docs/MANUAL-TESTS.md).

## Build

Requirements: Windows 11 and the .NET SDK pinned in `global.json` (10.0.x).

```powershell
dotnet build -warnaserror
dotnet test
```

The build treats warnings as errors and must stay at zero warnings.

## Run

```powershell
dotnet run --project src/TaskDesk.App
```

Run it once from the published folder (below), then turn on "Start with Windows" in Settings if you want it at logon. Auto-start writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TaskDesk` as `"<path to TaskDesk.App.exe>" --minimized`; the value is rewritten on every start so moving the folder is safe. Windows Settings, Apps, Startup can still disable it independently and TaskDesk does not fight that.

`--minimized` starts hidden in the tray. Never run TaskDesk as administrator: Windows does not deliver notifications to elevated processes.

## Publish

One publish profile, `win-x64`: self-contained (.NET and the Windows App SDK runtime inside the folder), unpackaged, ReadyToRun compiled, about 239 MB. It writes to `publish/win-x64/` (git-ignored):

```powershell
dotnet publish src/TaskDesk.App -p:PublishProfile=win-x64
```

Copy the whole folder anywhere, for example `%LOCALAPPDATA%\Programs\TaskDesk`, and run `TaskDesk.App.exe`. Nothing needs installing: neither .NET nor a Windows App Runtime. The profile is in `src/TaskDesk.App/Properties/PublishProfiles/`. Every publish leaves out the Windows App SDK files TaskDesk never loads (AI/ML, Widgets, imaging); the rules are in `src/TaskDesk.App/WindowsAppSdkPayload.targets` and the background in [ADR 0002](docs/adr/0002-notification-delivery.md) and [ADR 0004](docs/adr/0004-port-to-winui.md).

## Data location

Everything lives under `%LOCALAPPDATA%\TaskDesk`:

| What | Where |
|---|---|
| Database | `taskdesk.db` |
| Settings | `settings.json` |
| Backups | `backups\taskdesk-YYYYMMDD-HHmmss.db` (newest 10) |
| Logs | `logs\` (daily, newest 14) |

Settings has "Open data folder", Export, Import and "Back up now".

To use another folder (for example to try the app without touching your real data), set the environment variable `TASKDESK_DATA_DIR` to that folder before launching. When it is unset or blank the default location is used.

Setting `TASKDESK_DATA_DIR` to a non-blank value also turns on sandbox mode, so a trial run leaves the rest of the machine alone: the Run key is never read or written, so the "Start with Windows" setting has no effect, and reminders use tray balloons instead of registering Windows app notifications (no AppUserModelId keys, COM class or icon file are left behind). The `Runtime mode` line in the startup log shows which mode is active. To register Windows app notifications while using another data folder, also set `TASKDESK_SANDBOX=0`; that never re-enables the Run key. In a normal run, turning "Start with Windows" off removes the Run entry only if it points at the running exe.

## Windows App SDK

TaskDesk is a WinUI 3 app ([ADR 0004](docs/adr/0004-port-to-winui.md)), and Reminders use Windows app notifications from the same Windows App SDK. The app references `Microsoft.WindowsAppSDK` with `WindowsAppSDKSelfContained=true` and is unpackaged (`WindowsPackageType=None`), so the runtime DLLs ship inside the publish folder and nothing needs installing. See [ADR 0002](docs/adr/0002-notification-delivery.md).

If registration of rich notifications fails for any reason, TaskDesk logs it, shows a banner in the main window and falls back to tray balloon notifications. Reminders keep working either way. If no notification appears, check Windows Focus Assist / Do Not Disturb and Settings, System, Notifications.

## Performance check

A 5,000-Task database can be generated with the developer tool (it refuses the real data folder and creates no Reminders):

```powershell
dotnet run --project tools/TaskDesk.SampleData -- "C:\temp\taskdesk-perf" 5000
$env:TASKDESK_DATA_DIR = "C:\temp\taskdesk-perf"
```

Sandbox mode keeps the test run away from your Run key. Measured results and method are in [ADR 0004](docs/adr/0004-port-to-winui.md); targets are cold start to visible window under 1.5 s and `--minimized` working set under 150 MB.

## Developer scripts

- `scripts/gate.ps1`: build with warnings as errors, then run all tests; exit 0 is green.
- `scripts/integrate-worktree.ps1`: apply a worktree's uncommitted diff onto a clean checkout and run the gate (see `docs/agents/implement-spec.md`).
- `scripts/measure-startup.ps1`: measure cold start and `--minimized` working set of a publish against fresh sample data (`-Publish <dir> [-Runs 7] [-Tasks 5000] [-Label <name>]`).
- `scripts/ui-tests.ps1`: the opt-in UI test suite (`tests/TaskDesk.UiTests`, FlaUI). It drives the real window against fresh temp data folders, so it needs an interactive desktop and takes over the keyboard and mouse for about a minute. The gate builds it but does not run it (`-Publish <dir>` to test a publish, `-Class <name>` for one class, `-Method <name>` for matching tests). It reruns each failed test once, labels it `FLAKE` or `FAILED`, and exits 5 when only flakes failed and 6 on a locked desktop.
- `scripts/run-sandbox.ps1`: start the app on a throwaway data folder for a look (`-Theme Light|Dark`, `-Width <px>`, `-SampleTasks <n>`, `-Ref <git ref>` to run another commit). The `/ui-check` Claude Code skill (`.claude/skills/ui-check/`) runs the UI tests, then drives this app with `winapp ui` and screenshots the states a change touches.
- `scripts/compare-shots.ps1`: put before and after screenshots side by side with the changed pixels marked (`-Before <dir> -After <dir>`).
- `scripts/verify-toast.ps1`: owner-run check that a real Windows toast appears for a due Reminder; it registers a notification AppUserModelId for that exe (`-Publish <dir> [-DryRun]`).

## Known limits

- The publish is about 239 MB: most of it is the .NET runtime, WinUI and the 25 MB Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`).
- With 5,000 Tasks, cold start to a visible window was measured at about 0.9 s, and the `--minimized` working set at about 214 MB, over its target; the working set target is not a hard requirement (ADR 0004).
- Notifications are not delivered when TaskDesk runs elevated, or while Windows Do Not Disturb is on.
- The default global hotkey `Ctrl+Alt+Space` may already belong to another application. TaskDesk logs the conflict and shows it in Settings; choose another combination there.
- Subtasks are one level deep, Notes are plain text, and data is local to one PC (use Export and Import to move it).
- The Windows registration of the app's notification identity (`HKCU\Software\Classes\AppUserModelId`) is not removed on exit.
