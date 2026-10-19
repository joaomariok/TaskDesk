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

## Install

Run `Setup.exe` (`TaskDesk.App-win-Setup.exe`). It installs TaskDesk for your user only, with no admin rights, into `%LOCALAPPDATA%\TaskDesk.App`, adds a TaskDesk shortcut to the Start menu (no Desktop shortcut) and an entry in Apps & features, then starts TaskDesk. Your data stays in `%LOCALAPPDATA%\TaskDesk` (see [Data location](#data-location)), never in the install folder.

`Setup.exe` is not signed yet, so Windows SmartScreen may say "Windows protected your PC". Choose **More info**, then **Run anyway**.

To build a `Setup.exe` yourself, run `pwsh scripts/pack-installer.ps1 -Version <x.y.z>`. It publishes the `win-x64` profile at that version and packs it with Velopack (`vpk`, a local dotnet tool pinned in `dotnet-tools.json`); `TaskDesk.App-win-Setup.exe` and the release packages land in `releases/` (git-ignored), or in `-OutputDir <dir>`. The package id is `TaskDesk.App`, never `TaskDesk`: uninstalling deletes `%LOCALAPPDATA%\<package id>`, and `%LOCALAPPDATA%\TaskDesk` is the data folder.

### Moving from a copied folder

If you ran TaskDesk from a folder you copied by hand (for example `%LOCALAPPDATA%\Programs\TaskDesk`):

1. Quit TaskDesk from its tray icon (Exit).
2. Delete the old folder.
3. Run `Setup.exe`.

The installed TaskDesk uses the same data, and its first start points "Start with Windows" at the installed exe. Delete the old folder before you start the old copy again, or it takes the Run key back.

## Uninstall

Uninstall TaskDesk from **Settings > Apps > Installed apps** (Apps & features). Exit TaskDesk from its tray icon first, so an edit in progress is saved; uninstalling stops a running TaskDesk without saving it. Uninstalling removes the install folder `%LOCALAPPDATA%\TaskDesk.App`, the Start-menu shortcut, the "Start with Windows" entry (only when it points at the installed exe) and the app's Windows notification registration.

Your data stays: `%LOCALAPPDATA%\TaskDesk` keeps the database, settings, backups and logs, and installing TaskDesk again shows the same Tasks. To delete the data as well, delete that folder by hand after uninstalling:

```powershell
Remove-Item -Recurse "$env:LOCALAPPDATA\TaskDesk"
```

## Run

```powershell
dotnet run --project src/TaskDesk.App
```

"Start with Windows" is on by default: every normal start makes sure TaskDesk starts at logon, until you turn it off in Settings, and a saved "off" is kept. Auto-start writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TaskDesk` as `"<path to TaskDesk.App.exe>" --minimized`; the value is rewritten on every start so moving the folder is safe. Windows Settings, Apps, Startup can still disable it independently and TaskDesk does not fight that.

`--minimized` starts hidden in the tray. Never run TaskDesk as administrator: Windows does not deliver notifications to elevated processes.

## Publish

One publish profile, `win-x64`: self-contained (.NET and the Windows App SDK runtime inside the folder), unpackaged, ReadyToRun compiled, about 239 MB. It writes to `publish/win-x64/` (git-ignored):

```powershell
dotnet publish src/TaskDesk.App -p:PublishProfile=win-x64
```

To install TaskDesk, use `Setup.exe` (see [Install](#install)). The publish folder also runs as it is: run `TaskDesk.App.exe` from it. Nothing needs installing: neither .NET nor a Windows App Runtime. The profile is in `src/TaskDesk.App/Properties/PublishProfiles/`. Every publish leaves out the Windows App SDK files TaskDesk never loads (AI/ML, Widgets, imaging); the rules are in `src/TaskDesk.App/WindowsAppSdkPayload.targets` and the background in [ADR 0002](docs/adr/0002-notification-delivery.md) and [ADR 0004](docs/adr/0004-port-to-winui.md).

## Release

Pushing a tag `vX.Y.Z` cuts a release (decision: [ADR 0005](docs/adr/0005-velopack-installer.md)):

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The `Release` workflow (`.github/workflows/release.yml`) takes the version from the tag, runs the gate, packs with `scripts/pack-installer.ps1` (which first downloads the previous release, so a delta package is made against it) and uploads `TaskDesk.App-win-Setup.exe`, the full and delta packages and the feed to a GitHub Release with the workflow's `GITHUB_TOKEN`. If the gate fails, no release is created. A tag that is not exactly three numbers (`v1.2.3`) fails. Builds are unsigned.

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
- `scripts/pack-installer.ps1`: build a local `Setup.exe` and release packages for a version (`-Version <x.y.z> [-OutputDir <dir>] [-DownloadFrom <repo url>]`), see [Install](#install). The gate does not need it.
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
- An installed TaskDesk keeps its Windows notification registration (`HKCU\Software\Classes\AppUserModelId\velopack.TaskDesk.App`) on exit, so toasts in Action Center still open it; uninstalling removes it. A run that is not installed (a Debug build or a publish folder) removes its registration on exit, but one that is killed or crashes leaves it behind.
