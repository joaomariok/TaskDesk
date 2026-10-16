# Notification delivery via Windows App SDK app notifications

**Status: DRAFT. Several findings are pending owner verification (marked below). Do not treat this ADR as accepted until they are filled in.**

Reminders are delivered as Windows toasts through the Windows App SDK `AppNotificationManager` (`Microsoft.Windows.AppNotifications`), from an unpackaged, self-contained WPF .NET 10 app, with a tray balloon fallback behind `INotificationService` (SPEC 7.3). The spike in `spikes/NotificationSpike/` (outside `TaskDesk.slnx`) exercises this.

## Established from documentation and from building

- **Package:** `Microsoft.WindowsAppSDK` **1.8.260921001**, the latest stable 1.x on nuget.org when checked. Stable 2.x versions also exist (2.0.1 to 2.5.1); they were not evaluated, and 1.8.x was chosen because its notification API is the documented one. Revisit 2.x before M1 if wanted.
- **Unpackaged registration:** `<WindowsPackageType>None</WindowsPackageType>`; call `AppNotificationManager.Default.Register()` at startup (after subscribing to `NotificationInvoked`) and `Unregister()` on exit. The spike builds and publishes with these APIs, so the types and members exist in this package version: `AppNotificationManager`, `AppNotificationBuilder`, `AppNotificationButton`, `AppNotificationActivatedEventArgs`, `AppInstance.GetActivatedEventArgs()`, `ExtendedActivationKind.AppNotification`.
- **Self-contained publish:** `<SelfContained>true</SelfContained>` with `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` and `win-x64`. `dotnet publish` succeeds with no warnings. The output is about 277 MB and contains the Windows App SDK DLLs (`Microsoft.WindowsAppRuntime.dll`, the AppNotifications projections) next to the exe. Most of that size is `onnxruntime.dll` (about 22 MB) and other AI/ML payload that the full `Microsoft.WindowsAppSDK` metapackage pulls in; referencing only the Foundation sub-package would likely shrink it (not tried).
- **Activation arguments:** arguments are set when building the toast (`AddArgument` on the builder for the body click, on each `AppNotificationButton` for buttons) and come back as the `Arguments` dictionary of `AppNotificationActivatedEventArgs`.
  - Running process: the `NotificationInvoked` event delivers them.
  - Process not running: Windows launches the exe via the COM server that `Register()` set up; the new process reads them from `AppInstance.GetCurrent().GetActivatedEventArgs()` when `Kind == ExtendedActivationKind.AppNotification` (`Data` is an `AppNotificationActivatedEventArgs`). The spike implements both paths.
- **Product gate unaffected:** the spike has its own `Directory.Build.props` and `Directory.Packages.props` so it does not inherit the product's central package management or warnings-as-errors. `dotnet build -warnaserror` and `dotnet test` on the solution still pass (28 tests).
- **Test machine:** the owner's PC already has Windows App Runtime packages installed (1.1 to 1.8 and 2.x, found via `Get-AppxPackage`). Question (d) therefore cannot be answered on this PC without removing them or using a clean machine or VM.

## Pending owner verification

Nothing below has been observed. Fill in the result next to each item.

- **(a) Toast appears** from the unpackaged self-contained spike: PENDING OWNER VERIFICATION.
- **Buttons** (Mark done, Snooze 10 min, Snooze 1 h) are shown and each raises the expected `action` argument: PENDING OWNER VERIFICATION.
- **(b) Click while running** raises `NotificationInvoked` in the same process: PENDING OWNER VERIFICATION.
- **(c) Click while the app is closed** launches the app and the arguments are read from the activation args: PENDING OWNER VERIFICATION.
- **Toast title, icon and app name** with no Start-menu shortcut (does it show the exe name and icon, or something generic): PENDING OWNER VERIFICATION.
- **(d) Behaviour without the Windows App SDK runtime / Singleton package installed** (does `Register()` throw, does `Show()` fail silently, is the error catchable for the fallback): PENDING OWNER VERIFICATION, needs a clean machine or VM.

## Decision (provisional)

Keep the SPEC 7.3 design: primary `AppSdkNotificationService`, fallback tray balloon on any registration failure. If (a) to (c) pass, no change to SPEC 7.3 is needed. If (d) shows the runtime must be installed, the README documents the exact installer and the fallback covers its absence. If (a) fails, the tray balloon becomes the primary and this ADR is rewritten.

## Consequences

- The activation handler must handle two entry points (event in the running process, activation args at startup), so a single parser of toast arguments should live in one place.
- Toast arguments are plain key/value strings, so Reminder buttons can carry `action` and the task id without any server-side state.
- Registration must happen on every start, before the first toast is shown.
- TaskDesk must never run elevated (notifications are unsupported for elevated processes).
- A large self-contained output (about 277 MB with the full metapackage) is a cost of the chosen package; narrowing the reference is a possible follow-up.
