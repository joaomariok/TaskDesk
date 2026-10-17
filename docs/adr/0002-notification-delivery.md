# Notification delivery via Windows App SDK app notifications

**Status: Accepted (2026-10-16).** The owner verified (a) to (c) with the spike; (d) remains unverified (see below).

Reminders are delivered as Windows toasts through the Windows App SDK `AppNotificationManager` (`Microsoft.Windows.AppNotifications`), from an unpackaged, self-contained WPF .NET 10 app, with a tray balloon fallback behind `INotificationService` (SPEC 7.3). The spike in `spikes/NotificationSpike/` (outside `TaskDesk.slnx`) exercises this.

## Established from documentation and from building

- **Package:** `Microsoft.WindowsAppSDK` **1.8.260921001**, the latest stable 1.x on nuget.org when checked. Stable 2.x versions also exist (2.0.1 to 2.5.1); they were not evaluated, and 1.8.x was chosen because its notification API is the documented one. Revisit 2.x before M1 if wanted.
- **Unpackaged registration:** `<WindowsPackageType>None</WindowsPackageType>`; call `AppNotificationManager.Default.Register()` at startup (after subscribing to `NotificationInvoked`) and `Unregister()` on exit. The spike builds and publishes with these APIs, so the types and members exist in this package version: `AppNotificationManager`, `AppNotificationBuilder`, `AppNotificationButton`, `AppNotificationActivatedEventArgs`, `AppInstance.GetActivatedEventArgs()`, `ExtendedActivationKind.AppNotification`.
- **Self-contained publish:** `<SelfContained>true</SelfContained>` with `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` and `win-x64`. `dotnet publish` succeeds with no warnings. The output is about 277 MB and contains the Windows App SDK DLLs (`Microsoft.WindowsAppRuntime.dll`, the AppNotifications projections) next to the exe. Most of that size is `onnxruntime.dll` (about 22 MB) and other AI/ML payload that the full `Microsoft.WindowsAppSDK` metapackage pulls in; the product itself no longer uses the metapackage (see the next item).
- **Package choice and publish size (ticket 20, measured):** the product references `Microsoft.WindowsAppSDK.Foundation` 1.8.260803002 and `Microsoft.WindowsAppSDK.Runtime` 1.8.260921001, not the full metapackage. Foundation alone gives a smaller publish (about 196 MB without ReadyToRun, 240 MB with it), but on the dev PC `AppNotificationManager.Register()` then threw `COMException 0x8007007E` ("Unable to load resource dll Microsoft.WindowsAppRuntime.Insights.Resource.dll"), because that DLL is only copied into the output when the Runtime package is referenced, and `NotificationDelivery` fell back to the tray balloon. With the Runtime package added, the DLL is published, `Register()` succeeds (log: "App notifications registered") and the self-contained ReadyToRun `win-x64` publish is about 323 MB (adds ONNX Runtime, DirectML and WinUI binaries the app does not use). The owner's toast checks below were made with the spike, not with this product build. Shrinking this without losing the DLL is tracked in #21. The registration leaves `HKCU\Software\Classes\AppUserModelId` entries (a key derived from the exe path and a GUID key with DisplayName and IconUri) behind after exit.
- **Activation arguments:** arguments are set when building the toast (`AddArgument` on the builder for the body click, on each `AppNotificationButton` for buttons) and come back as the `Arguments` dictionary of `AppNotificationActivatedEventArgs`.
  - Running process: the `NotificationInvoked` event delivers them.
  - Process not running: Windows launches the exe via the COM server that `Register()` set up; the new process reads them from `AppInstance.GetCurrent().GetActivatedEventArgs()` when `Kind == ExtendedActivationKind.AppNotification` (`Data` is an `AppNotificationActivatedEventArgs`). The spike implements both paths.
- **Product gate unaffected:** the spike has its own `Directory.Build.props` and `Directory.Packages.props` so it does not inherit the product's central package management or warnings-as-errors. `dotnet build -warnaserror` and `dotnet test` on the solution still pass (28 tests).
- **Test machine:** the owner's PC already has Windows App Runtime packages installed (1.1 to 1.8 and 2.x, found via `Get-AppxPackage`). Question (d) therefore cannot be answered on this PC without removing them or using a clean machine or VM.

## Owner verification (2026-10-16, spike, Windows 11, from `NotificationSpike.log`)

- **(a) Toast appears:** yes. `Register()` succeeded, and the toast showed its title, body and the three buttons (Mark done, Snooze 10 min, Snooze 1 h).
- **Buttons:** each one delivered the expected argument (`action=done`, `snooze10`, `snooze60`).
- **(b) Click while running:** yes. Button clicks raised `NotificationInvoked` in the running process. If several registered processes run, Windows can deliver the click to a different one from the process that showed the toast, so TaskDesk's single-instance rule matters.
- **(c) Click while the app is closed:** yes, verified for the body click. The spike had called `Unregister()` and exited. Clicking the toast in the notification center started a new process with `Activation kind at startup: AppNotification` and the arguments `task=spike-123`. A button click on a toast from a closed app was not tested on its own, because another spike process was still running and received it. It uses the same COM activation path.
- **App name and icon with no Start-menu shortcut:** the toast header shows the exe-derived name (`NotificationSpike`) and a generic icon. The product registration records `DisplayName=TaskDesk`.
- **(d) Behaviour without an installed Windows App Runtime:** not verified, because this PC already has the runtimes. The product now publishes the runtime self-contained, which lowers the risk. A `Register()` failure is caught and falls back to the tray balloon.
- **Spike flaw found:** the spike's "Show toast in 15 s" waits inside its own process, so closing the window before the delay ends means no toast is ever shown. Test (c) by showing a toast and closing the app before clicking it.

## Decision

Keep the SPEC 7.3 design: `AppSdkNotificationService` is the primary path, with the tray balloon as fallback on any registration or show failure. (a) to (c) passed, so SPEC 7.3 needs no change.

## Consequences

- The activation handler must handle two entry points (event in the running process, activation args at startup), so a single parser of toast arguments should live in one place.
- Toast arguments are plain key/value strings, so Reminder buttons can carry `action` and the task id without any server-side state.
- Registration must happen on every start, before the first toast is shown.
- TaskDesk must never run elevated (notifications are unsupported for elevated processes).
- A large self-contained output (about 323 MB with Foundation plus Runtime) is a cost of the chosen packages; trimming it is tracked in #21.
