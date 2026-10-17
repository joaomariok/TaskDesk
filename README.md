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

```powershell
dotnet publish src/TaskDesk.App -p:PublishProfile=win-x64
```

This produces a self-contained `win-x64` folder at `publish/win-x64/` (git-ignored), ReadyToRun compiled, about 320 MB (see "Known limits"). Copy the whole folder anywhere, for example `%LOCALAPPDATA%\Programs\TaskDesk`, and run `TaskDesk.App.exe`. No .NET install is needed. The profile is `src/TaskDesk.App/Properties/PublishProfiles/win-x64.pubxml`.

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

## Windows App SDK

Reminders use Windows app notifications from the Windows App SDK. The app references `Microsoft.WindowsAppSDK.Foundation` and `Microsoft.WindowsAppSDK.Runtime` with `WindowsAppSDKSelfContained=true` and an unpackaged app (`WindowsPackageType=None`), so the runtime DLLs ship inside the publish folder and nothing needs installing. See [ADR 0002](docs/adr/0002-notification-delivery.md); it is still a draft until the owner has run the notification checks on a clean machine.

If registration of rich notifications fails for any reason, TaskDesk logs it, shows a banner in the main window and falls back to tray balloon notifications. Reminders keep working either way. If no notification appears, check Windows Focus Assist / Do Not Disturb and Settings, System, Notifications.

## Performance check

A 5,000-Task database can be generated with the developer tool (it refuses the real data folder and creates no Reminders):

```powershell
dotnet run --project tools/TaskDesk.SampleData -- "C:\temp\taskdesk-perf" 5000
$env:TASKDESK_DATA_DIR = "C:\temp\taskdesk-perf"
```

Pre-create `settings.json` in that folder with `{"startWithWindows":false}` first, so the test run cannot touch your Run key. Measured results and method are in the ticket report for issue #20; targets are cold start to visible window under 1.5 s and `--minimized` working set under 150 MB.

## Known limits

- The publish folder is large (about 320 MB) because the Windows App SDK runtime brings ONNX Runtime, DirectML and WinUI binaries that TaskDesk does not use. Trimming is a possible follow-up.
- Cold start to a visible window with 5,000 Tasks was measured at about 1.5 to 1.7 s, and the `--minimized` working set at about 170 MB. Both are slightly over their targets; the working set target is not a hard requirement.
- Notifications are not delivered when TaskDesk runs elevated, or while Windows Do Not Disturb is on.
- The default global hotkey `Ctrl+Alt+Space` may already belong to another application. TaskDesk logs the conflict and shows it in Settings; choose another combination there.
- Subtasks are one level deep, Notes are plain text, and data is local to one PC (use Export and Import to move it).
- The Windows registration of the app's notification identity (`HKCU\Software\Classes\AppUserModelId`) is not removed on exit.
