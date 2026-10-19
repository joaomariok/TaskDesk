# WinUI app

TaskDesk is a WinUI 3 app (ADR `docs/adr/0004-port-to-winui.md`). The WPF app it replaced was deleted at the swap (#68).

## Layout

- `src/TaskDesk.App`: the shell exe, `TaskDesk.App.exe`. Start-up, tray, hotkey, session end and the window lifecycle live in `Shell/`, toasts and the balloon fallback in `Notifications/`. `TaskDesk.Views.Tests` links the shell sources it tests with `<Compile Include Link>`.
- `src/TaskDesk.Views`: every view, window, control, style and dialog. It references Core and Presentation, never the shell.
- `tests/TaskDesk.Views.Tests`: an in-process WinUI test app (MSTest.Sdk, `[UITestMethod]`), part of the gate. Pattern: build the ViewModel over `TempDatabase` or fakes, `new MainWindow(vm)`, `Activate()`, `await vm.LoadAsync(ct)`, `UiWait.UntilAsync` until containers realize, assert through `VisualTree`, `Close()` in `finally`. Keys go through each view's `TryShortcut(key, modifiers)` seam.
- `tests/TaskDesk.UiTests`: the opt-in FlaUI suite. Run it with `pwsh scripts/ui-tests.ps1`.
- To look at the app: `pwsh scripts/run-sandbox.ps1`.
- Keep `<UseWinUI>true</UseWinUI>` literal in WinUI csproj files: the XAML guardrail tests in `TaskDesk.Core.Tests` find WinUI projects by it.

## XAML

- `x:Bind` takes an explicit `Mode` (WUI2011); a nested path such as `ViewModel.DetailTask.ListName` also takes a `FallbackValue` (WUI2010). `OneWay` needs a property that raises change notifications (WMC1506).
- An `x:Bind` function call with a path argument, such as `ShownWithText(ViewModel.Error)`, takes a `FallbackValue` equal to what the function returns for null: x:Bind skips the call when an argument is null and the old value stays (`WinUiXamlGuardrailTests`). `FallbackValue` takes only a string or `{x:Null}`.
- Colours come from `{ThemeResource}` only. A new theme brush is a `SolidColorBrush` in each theme dictionary (Default, Light, Dark, HighContrast) of `Styles/Foundation.xaml`; a `StaticResource` alias to a Fluent brush ignores a window's `RequestedTheme`.
- An implicit style applies to its exact type only. A derived control (`CommandCheckBox`, `CommandToggleButton`) gets its own `BasedOn` style in `Foundation.xaml`.
- An element with keyboard accelerators sets `KeyboardAcceleratorPlacementMode="Hidden"`, or the accelerator text shows in its tooltip.
- Every new window calls `ThemeService.Register(window)`. `MainView` draws no background, so Mica shows.
- Row UIA names are set in `ContainerContentChanging` (`TaskListView`, `NavigationPane.Lists`).

## Lifetime

Deferred work belongs to the view model, not the view: a view starts no timers of its own, and its window ends that work when it goes.

- A view model runs its deferred work through `ViewModelLifetime` (Presentation). Its `StopAsync()` latches once: after it no job starts, and a job it cancelled ends quietly, also with `ObjectDisposedException` from a database already let go of. Before the stop every failure surfaces. The stop is idempotent and completes once the cancelled jobs have ended.
- Every async entry of `MainViewModel` goes through its lifetime: the search, `LoadAsync`, the Smart View count refresh, the due refresh timer, the detail save debounce, the Undo bar timer and the completion hold. `MainViewModel.StopAsync()` first saves a pending detail edit, then stops; after it a detail edit saves nothing. The exit therefore has no save step of its own: `PrepareForExitAsync`, then the host stop. After `MainViewModel.StopAsync()` none of them touches a service or moves a row; the due refresh timer is disposed and never armed again. Because `LoadAsync` does nothing after the stop, the reloads that pass no token (a List edited, the quick-add card's Task created, an import) do nothing after it either.
- `MainViewModel` owns the search debounce on the injected `TimeProvider` and posts the tick back to the UI thread, as the due refresh does. A `SearchText` change queues the search; the view model's own clears (opening a List, opening a Task from a Reminder, Clear search) load at once and drop it.
- A new timer or load in a view model runs through its lifetime too (`_lifetime.RunAsync`), and a timer it arms checks `IsStopped` first, so it cannot outlive the window.
- `ListsViewModel` (the sidebar's Lists) has its own lifetime: its load and its commands (create, rename, colour, move, delete) do nothing after its `StopAsync()`. Its rename and delete dialogs stay outside the lifetime, so a stop never waits on the owner. `NavigationPane`'s handlers need no stop of their own: they call into the view models.
- The window stops its view models, `MainViewModel` and `ListsViewModel`, on `Closed` (not awaited: the latch and cancel are synchronous) and at the exit (`PrepareForExitAsync` disables the sidebar, then awaits both). WinUI raises `TextChanged` and `Unloaded` after `Close` has returned, so a view's own handlers cannot be the stop.
- Start loads from events, never from constructor-time property sets. Tests dispose `TempDatabase` right after closing the window, and orphaned work then throws in an `async void` handler and kills the whole test host (0xC000027B). Drive a debounce with the database's fake clock (`db.Clock.Advance(MainViewModel.SearchDelay)`), not a real-time wait. Run the whole `TaskDesk.Views.Tests` project before reporting.

## Manual checks

What the gate cannot drive in-process. Run each after changing the code it names.

- Mixed-DPI restore (`Shell/WindowLayoutService.cs`): on a desk with monitors at two scales (say 100 % and 150 %), put the main window on the monitor at the other scale than the primary, resize it, exit from the tray and start again with `pwsh scripts/run-sandbox.ps1`. It opens on that monitor at the same position and physical size, and the day's log has no "never took its monitor's scale" warning. Repeat with the window on the primary monitor, then once with the window maximized.
- Exit during start-up (`App.OnLaunched`, `ShellController.LaunchAsync`), on a normal (not sandbox) run, since sandbox mode shows balloons only: click a Reminder toast's Open while TaskDesk is not running, and choose Exit from the tray as soon as it appears. The app ends, no TaskDesk process is left, and the day's log has no "Unhandled UI exception". Repeat with Mark done: the Task is completed at the next start.

## Gotchas

- Rail and docked detail pane follow the window width with its frame (`MainWindow.WindowWidth`, `PaneLayout`), as the WPF window measured it, so saved layouts carry over.
- A closed `LeftCompact` `NavigationView` left-aligns its `PaneFooter` (`FooterContentBorder`), so footer content shrinks to its own width; `NavigationPane.ShowCompact` sets the footer to `CompactPaneLength`.
- Fluent's control templates and sizes (`NavigationViewItemOnLeftIconBoxHeight` and the like) are in `~/.nuget/packages/microsoft.windowsappsdk.winui/<version>/lib/native/Microsoft.UI/Themes/generic.xaml`.
- Recycled `ListView` containers stay in the visual tree: count only those with `list.IndexFromContainer(c) >= 0`.
- A hidden pane's `x:Bind` values update after layout: wait for `pane.ActualWidth > 0` before asserting.
- H.NotifyIcon.WinUI's PopupMenu runs each item's `Command` and never raises `Click` (`Shell/TrayMenu.cs`).
- A borderless `OverlappedPresenter` keeps a WS_DLGFRAME frame. Quick add removes it with a WM_NCCALCSIZE subclass and `DWMWA_BORDER_COLOR` none (`QuickAddWindow.xaml.cs`).
- A message-only window gets no WM_QUERYENDSESSION: `SessionEndHook` uses a top-level `MessageWindow`.
- The WinRT clipboard refuses a window without focus; `WinUiClipboardService` uses the Win32 clipboard.
- FlaUI's `Mouse.Drag` and cursor jumps send WinUI no pointer moves: drag with `AppSession.DragSlowly` (SendInput steps).
