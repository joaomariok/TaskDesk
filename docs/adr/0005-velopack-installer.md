# Installer and release: Velopack, unpackaged, released by a version tag

**Status: Accepted (2026-10-18). Spec #87; go/no-go on #89.**

TaskDesk was a published folder that the owner copied by hand. v1 had "installer/MSIX, auto-update" out of scope. The owner now wants a `Setup.exe` that installs for the current user, shows up in Apps & features and can be released by pushing a tag. This record covers the tool, the rules that keep user data safe, and what the go/no-go on the owner's machine found.

## Decision

- **Velopack, not MSIX.** The app stays unpackaged, as ADR 0002 and ADR 0004 decided: toast delivery, start-up and the self-contained Windows App SDK are built for an unpackaged exe. MSIX would give the app package identity and change all three, and needs a signing certificate to install at all. Velopack installs the unpackaged publish folder as it is, per user, with no admin rights, into `%LOCALAPPDATA%\TaskDesk.App`.
  - The `Velopack` NuGet package and the `vpk` tool are both pinned at 1.2.161 (`Directory.Packages.props`, `dotnet-tools.json`). `Program.cs` runs `VelopackApp.Build().Run()` first in `Main`, so install and uninstall hooks run before the app starts.
  - `vpk pack` is always given `--runtime win-x64`; without it the package is marked x86.
  - Builds are unsigned. `Setup.exe` triggers SmartScreen ("More info", "Run anyway"); the README says so. Signing is a later decision.
- **The package id is never the data folder's name.** The package id is `TaskDesk.App`. Uninstall deletes `%LOCALAPPDATA%\<package id>`, and the data folder is `%LOCALAPPDATA%\TaskDesk`. A package id of `TaskDesk` would make uninstall delete the user's database. `InstallerGuardrailTests` fails if the pack script's `--packId` equals the data folder's name.
  - Uninstall keeps `%LOCALAPPDATA%\TaskDesk`. Removing the data is the user's decision.
- **One pack script for local and CI.** `scripts/pack-installer.ps1` publishes and packs; the release workflow calls it with `-DownloadFrom`, which downloads the latest release first so `vpk pack` also writes a delta package. No `vpk pack` argument is written twice.
- **A release is a pushed `vX.Y.Z` tag.** `.github/workflows/release.yml` runs only for `v*` tags, takes the version from the tag (anything but three numbers fails), runs the gate on `windows-latest`, packs, and uploads `Setup.exe`, the full and delta packages and the feed with `vpk upload github --publish`, using the workflow's `GITHUB_TOKEN` (`contents: write`). The steps are in order in one job, so a failing gate stops the job before a release exists.
  - The gate runs on the hosted runner the same way `ci.yml` runs it, with the display set to 1920 x 1080 because the in-process WinUI tests open windows and the docked-pane tests need a wide screen.
  - Pre-release tags (`v1.0.0-beta.1`) are not supported by the workflow yet; the pack script would accept them.

## Toasts and the AppUserModelId (go/no-go, #89)

The owner installed 0.1.0, upgraded to 0.1.1 and uninstalled, on the owner's own machine (not Windows Sandbox). The result was go.

- Installed, the app runs from `%LOCALAPPDATA%\TaskDesk.App\current\TaskDesk.App.exe`, logs `Runtime mode: normal`, keeps its data in `%LOCALAPPDATA%\TaskDesk` and writes nothing to the install folder.
- The Windows App SDK registers the app's notifications under `HKCU\Software\Classes\AppUserModelId\velopack.TaskDesk.App`: DisplayName `TaskDesk`, an icon and a COM activator whose `LocalServer32` points at `current\TaskDesk.App.exe`. Velopack's `velopack.<package id>` AppUserModelId is the one in use, so the toast header reads "TaskDesk" with the app icon. The log said `App notifications registered`.
- The "Start with Windows" Run value points at `current\TaskDesk.App.exe --minimized` and kept working after the upgrade. Each upgrade moves `current\`, and the app rewrites the value on every start (README, Run).
- Uninstall removed the install folder, the shortcut and the Apps & features entry, and kept the data. It left behind the Run value, the AppUserModelId key, the COM class and the icon file. Cleaning those up is #90.
- Every exe path that runs with toast registration on leaves one set of such entries behind, and `Unregister()` on exit removes none of them. #90 also makes runs that are not installed remove their registration on exit.

**Not answered:** ADR 0002 question (d), whether the app runs with no Windows App Runtime installed. The check did not run on a clean machine. The build is self-contained (`WindowsAppSDKSelfContained`), so the risk is low; it stays open.

## Consequences

- A release is one `git tag` and one `git push`; there is no manual upload.
- Updating from inside the app (`UpdateManager`) is not part of this record; the feed and delta packages it would use are already published.
- An unsigned `Setup.exe` can be flagged by SmartScreen and some antivirus tools until the builds are signed.
- The release workflow cannot be tried without pushing a tag. Its acceptance criteria are checked by pushing a test tag (a second one for the delta package) and deleting the test releases and tags afterwards.
