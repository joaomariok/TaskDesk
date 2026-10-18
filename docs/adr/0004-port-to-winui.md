# UI framework: port the app to WinUI 3, AOT-ready but JIT for now

**Status: Accepted (2026-10-17). Supersedes [0003](0003-ui-framework.md).**

ADR 0003 measured a WinUI 3 port and decided to stay on WPF. A WinUI build that can open the product database is not smaller than framework-dependent WPF, and it uses more memory. The owner now accepts those costs and ports the app to WinUI 3 anyway, for three reasons:

- **Native Fluent.** Windows 11 controls replace the hand-rolled ones: `InfoBar`, `SettingsCard`, `FluentDialog`, the command checkboxes and Mica.
- **Platform.** The Windows App SDK is where Windows desktop UI investment goes. The App project is small enough (about 2,400 lines) to move now.
- **A path to Native AOT.** The new code follows AOT rules from the start, so AOT later depends only on the EF Core Data layer (0003's blocker 1), not on the UI.

## Decision

- **Build.** WinUI 3, unpackaged (`WindowsPackageType=None`), self-contained, `win-x64`, JIT with ReadyToRun. The target framework (`net10.0-windows10.0.22000.0`) and the Windows App SDK 1.8 line are the same as today.
  - No trimming and no Native AOT while EF Core blocks them.
  - The app stays unpackaged, against the WinUI tooling's MSIX default, because toast delivery and start-up are built for unpackaged (ADR 0002).
- **AOT-ready code.**
  - `x:Bind` only: no `{Binding}` and no `DisplayMemberPath`. A runtime binding that cannot be compiled becomes code-behind.
  - `ThemeResource` instead of `DynamicResource`, and `DispatcherQueue` for every UI-thread hop.
  - `TaskDesk.Presentation` takes no WinRT or CsWinRT dependency.
- **Look.** The layout and design tokens stay the same, but native WinUI controls replace the hand-rolled ones. The window keeps the standard title bar, with Mica. Community Toolkit packages (`GridSplitter`, `SettingsCard`) are allowed. DESIGN.md follows wherever native rendering differs.
- **Windows App SDK package.** `H.NotifyIcon.WinUI` brings in the full `Microsoft.WindowsAppSDK` metapackage. The publish payload filter keeps removing the AI/ML, Widgets and imaging payload (stage A). It stops removing WinUI (stage B).
- **Delivery.** The WPF app keeps shipping while the WinUI app is built beside it.
  - The WinUI app is an exe that holds only the shell: the app, host, tray, hotkey, single-instance and window lifecycle.
  - Views, controls, styles and dialogs go in a WinUI class library.
  - The last ticket deletes the WPF projects and gives the WinUI exe the `TaskDesk.App` name.
  - Until then, the WinUI app runs only in sandbox mode (`TASKDESK_DATA_DIR`). Its first run on real data is the owner's acceptance step at the swap.
- **Window layout** stays stored in device-independent pixels, so `settings.json` reads the same in both builds. The WinUI shell converts to physical pixels for `AppWindow`.
- **Tests.**
  - The main seam is an in-process WinUI test app (MSTest.Sdk `[UITestMethod]`, unpackaged, on Microsoft.Testing.Platform) inside the gate. It loads real views over real view models with fakes or a temp database. This depends on the first ticket proving that `dotnet test` runs it and fails on a failing test. Otherwise the fallback is the FlaUI suite, plus Core calculators and source-text tests.
  - The FlaUI suite keeps only what needs a real process or real input: restart persistence, second launch, the global hotkey, the quick-add window, the tray, drag, Shift+F10 and the theme start-up smoke.
  - A WPF test or FlaUI test is deleted only at the swap, against an old-to-new mapping table the owner approves.
- **No performance threshold.** The swap does not depend on start-up time or memory. The parity ticket measures the WinUI build against WPF with `scripts/measure-startup.ps1` and records the numbers here.

`spikes/WinUiAotSpike/` stays as the reference for a future AOT measurement.

## Measurement (parity ticket, 2026-10-17)

`scripts/measure-startup.ps1` on the same machine, Release publishes, 5,000 sample Tasks, sandbox mode. Cold start is the median of 7 launches to a visible main window after one discarded warm-up. Working set is the median of 3 `--minimized` launches, read after 12 s.

| Build | Publish | Cold start | Working set |
|---|---|---|---|
| WPF, framework-dependent | 52.3 MB, 99 files | 1663 ms (1588 to 1712) | 172.3 MB |
| WPF, self-contained | 222.6 MB, 494 files | 1625 ms (1590 to 1707) | 168.1 MB |
| WinUI, self-contained | 238.7 MB, 513 files | 922 ms (913 to 948) | 214.1 MB |

The WinUI build starts in about half the time. It uses about 42 to 46 MB more memory, and its publish is 16 MB larger than self-contained WPF.

The platform forced two deviations from the spec:
- **Session end.** A message-only window never receives `WM_QUERYENDSESSION`, so a hidden top-level window handles session end.
- **Resume.** `SystemEvents.PowerModeChanged` needs the WPF or WinForms framework, so resume is read from `WM_POWERBROADCAST`.

## Consequences

- Publish size and working set are expected to grow (0003: 226 to 281 MB self-contained against 137.7 MB framework-dependent; 176 to 194 MB against 168 MB). This is accepted.
- The test suite gains a second framework, MSTest, limited to the WinUI test app, because `[UITestMethod]` exists only there.
- README, DESIGN, PRODUCT, `docs/SPEC.md` and ADR 0002 describe WPF until the swap ticket updates them.
- One view-model change (owner decision on #56): WinUI dialogs and file pickers are async-only, so the four Presentation dialog interfaces (`IListDialogs`, `IOccurrenceDialogs`, `IExportDialogs`, `IImportDialogs`) return `Task`. The WPF implementations return completed tasks, the commands that call them await, and the Presentation tests changed only in their fakes.
