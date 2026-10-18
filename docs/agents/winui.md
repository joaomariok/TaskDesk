# WinUI app

The WinUI 3 port (ADR `docs/adr/0004-port-to-winui.md`) runs beside the WPF app until the swap (#68).

## Layout

- `src/TaskDesk.App.WinUI`: the shell exe, `TaskDesk.App.WinUI.exe`, root namespace `TaskDesk.App`. Start-up, tray, hotkey, session end and the window lifecycle live in `Shell/`. It runs in sandbox mode only (`TASKDESK_DATA_DIR` set). Shell sources shared with the WPF app are linked with `<Compile Include Link>`, never copied.
- `src/TaskDesk.Views`: every view, window, control, style and dialog. It references Core and Presentation, never the shell.
- `tests/TaskDesk.Views.Tests`: an in-process WinUI test app (MSTest.Sdk, `[UITestMethod]`), part of the gate. Pattern: build the ViewModel over `TempDatabase` or fakes, `new MainWindow(vm)`, `Activate()`, `await vm.LoadAsync(ct)`, `UiWait.UntilAsync` until containers realize, assert through `VisualTree`, `Close()` in `finally`. Keys go through each view's `TryShortcut(key, modifiers)` seam.
- `tests/TaskDesk.UiTests`: the opt-in FlaUI suite. `pwsh scripts/ui-tests.ps1 -WinUI` points it at the WinUI build.
- To look at the app: `pwsh scripts/run-sandbox.ps1 -WinUI`.
- Keep `<UseWinUI>true</UseWinUI>` literal in WinUI csproj files: the XAML guardrail tests in `TaskDesk.Core.Tests` find WinUI projects by it.

## XAML

- `x:Bind` takes an explicit `Mode` (WUI2011); a nested path such as `ViewModel.DetailTask.ListName` also takes a `FallbackValue` (WUI2010). `OneWay` needs a property that raises change notifications (WMC1506).
- Colours come from `{ThemeResource}` only. A new theme brush is a `SolidColorBrush` in each theme dictionary (Default, Light, Dark, HighContrast) of `Styles/Foundation.xaml`; a `StaticResource` alias to a Fluent brush ignores a window's `RequestedTheme`.
- An implicit style applies to its exact type only. A derived control (`CommandCheckBox`, `CommandToggleButton`) gets its own `BasedOn` style in `Foundation.xaml`.
- An element with keyboard accelerators sets `KeyboardAcceleratorPlacementMode="Hidden"`, or the accelerator text shows in its tooltip.
- Every new window calls `ThemeService.Register(window)`. `MainView` draws no background, so Mica shows.
- Row UIA names are set in `ContainerContentChanging` (`TaskListView`, `NavigationPane.Lists`).

## Lifetime

Async work a view starts (timers, handlers, loads) ends with its window: stop timers on `Unloaded`, and start loads from events, never from constructor-time property sets. Tests dispose `TempDatabase` right after closing the window, and an orphaned load then throws in an `async void` handler and kills the whole test host (0xC000027B). Run the whole `TaskDesk.Views.Tests` project before reporting.

## Gotchas

- Rail and docked detail pane follow the window width with its frame (`MainWindow.WindowWidth`, `PaneLayout`), as the WPF window measured it.
- Recycled `ListView` containers stay in the visual tree: count only those with `list.IndexFromContainer(c) >= 0`.
- A hidden pane's `x:Bind` values update after layout: wait for `pane.ActualWidth > 0` before asserting.
- H.NotifyIcon.WinUI's PopupMenu runs each item's `Command` and never raises `Click` (`Shell/TrayMenu.cs`).
- A borderless `OverlappedPresenter` keeps a WS_DLGFRAME frame. Quick add removes it with a WM_NCCALCSIZE subclass and `DWMWA_BORDER_COLOR` none (`QuickAddWindow.xaml.cs`).
- A message-only window gets no WM_QUERYENDSESSION: `SessionEndHook` uses a top-level `MessageWindow`.
- The WinRT clipboard refuses a window without focus; `WinUiClipboardService` uses the Win32 clipboard.
- FlaUI's `Mouse.Drag` and cursor jumps send WinUI no pointer moves: drag with `AppSession.DragSlowly` (SendInput steps).
