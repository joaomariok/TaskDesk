# UI framework: stay on WPF, do not port to WinUI 3 / Native AOT now

**Status: Accepted (2026-10-17): stay on WPF.** Based on the spike in `spikes/WinUiAotSpike/` (outside `TaskDesk.slnx`), measured on the owner's PC in one session together with the current WPF app.

The question was whether porting the WPF app to WinUI 3, possibly with Native AOT, would give a smaller publish folder and a faster cold start. The measurements say Native AOT would, but EF Core cannot run the product's Data layer under AOT without rewriting it. The WinUI builds that do run with the real database are not smaller than the framework-dependent WPF build, and they use more memory than WPF.

## What the spike is

- An unpackaged WinUI 3 app (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`) on .NET 10, `Microsoft.WindowsAppSDK` 1.8.260921001 (the same 1.8 line as the product), self-contained `win-x64`.
- It uses the real `TaskDesk.Core` and `TaskDesk.Data` by project reference: `AddTaskDeskData`, `IDatabaseInitializer`, `ITaskService.ListDefaultListTasksAsync` and `CreateTaskAsync`, and the product's Serilog file logging. It shows the 4,000 open Tasks of the Default List of a 5,000-Task sample database (from `tools/TaskDesk.SampleData`) in a `ListView`, which virtualizes through its default `ItemsStackPanel`. It also has a quick-add box and a tray icon (`H.NotifyIcon.WinUI` 2.4.1) with Show and Exit, and close hides the window to the tray.
- It does much less than the product. It has no navigation pane, detail pane, Generic Host, hotkey, reminder scheduler, single-instance pipe or app notification registration. Its start-up numbers are therefore a lower bound for a full port, not a like-for-like comparison.
- It refuses to start without `TASKDESK_DATA_DIR`, so it never opens the real data folder.
- `--no-data` replaces EF Core with 4,000 in-memory Tasks. This measures the WinUI shell on its own, including in the builds where EF Core cannot run.
- The publish variant is chosen with `-p:SpikeVariant=plain|r2r|trimmed|trimmed-rooted|aot`. `PublishTrimmed` and `PublishAot` are set inside the spike project rather than on the command line. As a global property they also turn on the trim analyzers in `TaskDesk.Core`, whose warnings-as-errors then fail the build (see the blockers).

## Method

- **Publish:** `dotnet publish spikes/WinUiAotSpike -c Release -r win-x64 --self-contained -p:SpikeVariant=<v>` after deleting the spike's `bin` and `obj`. WPF self-contained: `dotnet publish src/TaskDesk.App -p:PublishProfile=win-x64` (ReadyToRun). WPF framework-dependent: `dotnet publish src/TaskDesk.App -c Release -r win-x64 --self-contained false`.
- **Cold start (#20 method):** a stopwatch starts just before `Start-Process`, then polls every 5 ms until the process has a `MainWindowHandle` for which `IsWindowVisible` is true. Each series has one discarded warm-up run and 7 measured runs; the table shows the median, with min and max. Processes were stopped with `Stop-Process` after each run.
- **Working set:** start with `--minimized` (both apps support it), read `WorkingSet64` after 12 s, median of 3 runs.
- **Data:** each series used its own fresh copy of the same 5,000-Task database (4,000 open), with `TASKDESK_DATA_DIR` set, so every run was in sandbox mode.
- All series ran back to back in one session (2026-10-16 23:49 to 23:59) with no other TaskDesk process running.

## Results

| Build | Publish size | Files | Cold start to visible window, median (min to max) | Working set at `--minimized` | Runs on the 5,000-Task database? |
|---|---|---|---|---|---|
| WPF, self-contained, ReadyToRun (`PublishProfile=win-x64`) | 321.0 MB | 754 | 1,646 ms (1,609 to 1,765) | 168.0 MB | yes |
| WPF, framework-dependent | 137.7 MB | 359 | 2,142 ms (2,067 to 2,430) | 177.6 MB | yes |
| WinUI 1. plain (self-contained, JIT) | 226.0 MB | 543 | 1,521 ms (1,506 to 1,563) | 193.8 MB | yes |
| WinUI plain + ReadyToRun (extra, matches the WPF profile) | 281.2 MB | 543 | 841 ms (792 to 979) | 185.3 MB | yes |
| WinUI 2. `PublishTrimmed` | 136.0 MB | 387 | fails at start-up, see blockers | n/a | **no** |
| WinUI `PublishTrimmed`, EF Core and TaskDesk assemblies rooted (extra) | 137.7 MB | 392 | 2,238 ms (2,174 to 2,356) | 176.0 MB | yes |
| WinUI 3. `PublishAot` | 270.0 MB (131.6 MB without the 138.4 MB `.pdb`) | 289 | fails at start-up, see blockers | n/a | **no** |

The WinUI shell alone (`--no-data`: 4,000 in-memory rows, no EF Core; the size is the same build as above):

| Build | Cold start, median (min to max) | Working set at `--minimized` |
|---|---|---|
| plain | 449 ms (431 to 475) | 148.4 MB |
| plain + ReadyToRun | 290 ms (272 to 307) | 143.4 MB |
| `PublishTrimmed` | 715 ms (654 to 748) | 135.4 MB |
| `PublishAot` | 134 ms (134 to 150) | 112.8 MB |

What the numbers show:

- **The WPF baseline reproduces #20:** 321 MB against about 323 MB, 1.65 s against about 1.54 s, 168 MB against about 172 MB, and 137.7 MB framework-dependent against 139 MB.
- **EF Core dominates start-up once the shell is fast.** With ReadyToRun, the spike takes 841 ms with the database and 290 ms without it. Opening the database, building the model and running the first query cost about 550 ms.
- **Trimming makes start-up slower, not faster.** Plain 449 ms became 715 ms trimmed (no data), and 1,521 ms became 2,238 ms with the database. The likely cause is that ILLink rewrites the framework assemblies without their ReadyToRun code, so everything is JIT-compiled. Trimmed plus ReadyToRun was not measured.
- **Native AOT is the only large win,** at 134 ms and 113 MB for the shell. It is the one build that cannot open the product database.
- **No WinUI build that runs with the database uses less memory than WPF** (176 to 194 MB against 168 MB), although the spike does far less than the app.
- **Size:** about 38.5 MB of every WinUI publish is `onnxruntime.dll` and `DirectML.dll` from the full `Microsoft.WindowsAppSDK` metapackage. `H.NotifyIcon.WinUI` depends on that metapackage, and trimming and AOT cannot remove native DLLs. The AOT `.exe` is 27.9 MB; its `.pdb` would not ship.

## Trim and AOT warnings

Unique warnings from the publish logs, grouped by source. The default ILLink/ILC output collapses package warnings into one IL2104/IL3053 per assembly. The detailed counts come from a second publish with `-p:TrimmerSingleWarn=false`. No warnings were suppressed.

| Source | Plain | Trimmed (default / detailed) | AOT (default / detailed) |
|---|---|---|---|
| EF Core (`Microsoft.EntityFrameworkCore*`, its shared `System.SharedTypeExtensions` and the DI extensions) | 0 | 4 / 474 | 12 / 736 (271 are IL3050 "requires dynamic code") |
| H.NotifyIcon (`H.NotifyIcon`, `H.NotifyIcon.WinUI`) | 0 | 0 / 0 | 0 / 0 |
| Windows App SDK / CsWinRT (`WinRT.Runtime`, `Microsoft.Windows.SDK.NET`) | 0 | 2 / 35 (IL2081 in the CsWinRT ABI fallback paths) | 0 / 0 |
| TaskDesk product code reached by the spike (`TaskDeskDbContext.cs` lines 42, 62, 73: IL2026 from `HasIndex`/`HasKey` with anonymous types) | 0 | 3 / 3 | 3 / 3 |
| Serilog | 0 | 1 / 1 (IL2104) | 1 / 1 |
| Spike code | 0 | 0 / 0 | 0 / 0 |

In addition, when `PublishTrimmed=true` reaches the product projects (as a global property), `TaskDesk.Core`'s own trim analyzer reports IL2026 for reflection-based `System.Text.Json` in `Export/JsonExportFormat.cs` (lines 55, 59) and `Shell/SecondLaunch.cs` (lines 71, 98). Because the product treats warnings as errors, the build fails. The spike does not call that code, so it is trimmed away there, but a full port would hit it.

## Blockers

1. **EF Core cannot open the product database under Native AOT.** EF Core 10.0.13 was tried, with `dotnet-ef` 10.0.13, on a scratch copy of the repository; the product's Data project was not changed.
   - **Without a compiled model**, the first database access (`DatabaseInitializer` → `GetPendingMigrationsAsync`) throws `InvalidOperationException: Model building is not supported when publishing with NativeAOT. Use a compiled model.`
   - **`dotnet ef dbcontext optimize --precompile-queries --nativeaot`** generates the compiled model (15 files), but query precompilation fails with 116 errors. Of these, 46 are `Dynamic LINQ queries are not supported when precompiling queries`: queries composed over an `IQueryable` variable (`tasks.Where(...)` in the Smart Views and search) and through the shared `WithDetails()` extension. The other 70 are `UnreachableException: IdentifierName of type ParameterSymbol: cancellationToken` (also `taskId`, `tagId`, `deleted`, `t`) on ordinary queries such as `context.Lists.AnyAsync(l => l.IsDefault, cancellationToken)`.
   - **With the compiled model only** (no precompiled queries), the AOT exe crashes during model initialization with `NotSupportedException: 'ValueComparer.CreateDefault[System.DateOnly](Boolean)' is missing native code`, from `RecurrenceEntityType.Create` (the owned `Recurrence` type). The process is terminated.
   - `MigrateAsync` and `GetPendingMigrationsAsync` carry `RequiresDynamicCode` (IL3050). Whether migrations run under AOT was not reached, because the steps above fail first.
   - Getting past this means restructuring the Data layer's queries: no composed `IQueryable` variables, no shared include helper. It also needs a fix or workaround for the `DateOnly` comparer and the parameter-symbol failures, which look like EF Core issues. All of this is on features EF Core labels experimental. Rewriting the Data layer was out of scope.
2. **`PublishTrimmed` breaks EF Core at runtime.**
   - Plain trimming fails at start-up with `Cannot create an instance of value converter type 'UtcDateTimeConverter'` (`HaveConversion<UtcDateTimeConverter>()` creates it by reflection, and its constructor was trimmed).
   - Rooting `TaskDesk.Core` and `TaskDesk.Data` (`TrimmerRootAssembly`) then fails with `Constructor on type 'EntryCurrentValueComparer<Guid>' not found`. Only rooting the three EF Core assemblies as well makes it run. At that point it is 1.7 MB bigger, and slower to start than the untrimmed build.
3. **CsWinRT needs extra setup for `ItemsSource` under trimming and AOT, and fails silently without it.** Binding an `ObservableCollection<T>` to `ListView.ItemsSource` failed under AOT (`ArgumentException: Value does not fall within the expected range`) and under trimming (`NullReferenceException` in `WinRT.TypeExtensions.GetAbiToProjectionVftblPtr`). The CsWinRT source generator emitted no vtable for the collection until the project set `AllowUnsafeBlocks=true`; with it, the generator emitted one (the spike also declares `[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<TaskRow>))]`). No build warning pointed at this. Every collection the product binds would need the same check, and `{Binding}` (as opposed to `x:Bind`) would need `GeneratedBindableCustomProperty` types.
4. **Size is dominated by the Windows App SDK, not the UI framework.** WinUI cannot drop the ONNX Runtime/DirectML payload while `H.NotifyIcon.WinUI` pulls in the full metapackage. The product already ships the WinUI binaries through `Microsoft.WindowsAppSDK.Runtime` (ADR 0002), and ticket #21 already tracks shrinking that.
5. **Build environment (not a product blocker):** on this PC, Native AOT linking failed until `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` (`vswhere.exe`) was on `PATH`. `vcvarsall.bat` calls `vswhere.exe` by name, and the "not recognized" message ends up in the linker command. Native AOT also needs the Visual Studio C++ build tools on every machine that publishes, including CI.

## What a full port would involve

`src/TaskDesk.App` is about 2,400 lines of XAML and C#, and all of it is WPF-specific. `TaskDesk.Presentation` (CommunityToolkit.Mvvm view models), `TaskDesk.Core` and `TaskDesk.Data` have no WPF dependency and would be reused.

- **XAML:** 146 `{Binding}` uses across `MainWindow`, `QuickAddWindow`, `SettingsWindow`, `DetailPane`, `NavigationPane` and `TaskListArea` would move to `x:Bind`, or to `GeneratedBindableCustomProperty` view models for AOT. WinUI has no `Style.Triggers`/`DataTrigger`, used in `SettingsWindow` and `TaskListArea` (Overdue styling, validation), so these become visual states or converters.
- **Tray:** `Shell/HNotifyTrayIcon.cs` and `Notifications/TrayBalloonNotificationService.cs` move from `H.NotifyIcon.Wpf` to `H.NotifyIcon.WinUI` 2.4.1. That package exists for .NET 10 (it targets `net10.0-windows10.0.17763` and depends on `Microsoft.WindowsAppSDK` ≥ 1.6.250108002) and produced no trim or AOT warnings. In the spike, `ForceCreate` succeeded in every build, but the tray menu and balloons were not exercised.
- **Borderless quick-add:** `QuickAddWindow` uses `WindowStyle=None`, `ResizeMode=NoResize`, `Topmost` and `ShowInTaskbar=False`. In WinUI this becomes `AppWindow` with an `OverlappedPresenter` (no border or title bar, always on top, not resizable) and a window hidden from the switchers. These APIs were not exercised in the spike.
- **Drag-drop:** `TaskListArea` (Task manual order) and `NavigationPane` (List order) use `DragDrop.DoDragDrop` with a custom data format. They would be rewritten on `ListView` drag/reorder events, keeping `ITaskService.MoveTaskAsync` and `IListService.ReorderListsAsync`.
- **Theme:** `Shell/WpfThemeService.cs` uses WPF's experimental `Application.ThemeMode` (WPF0001). In WinUI the theme is `Application.RequestedTheme`, set before start-up, or `RequestedTheme` on each window's root element, so a live switch works differently.
- **Global hotkey:** `Shell/Win32HotkeyRegistrar.cs` receives `WM_HOTKEY` through a WPF `HwndSource`. It needs its own message-only window (P/Invoke) or a subclassed WinUI window procedure. `RegisterHotKey` itself is unchanged.
- **Other WPF-specific code:**
  - `App.xaml.cs`: `OnStartup`, `ShutdownMode`, `Dispatcher` and `MessageBox` move to `OnLaunched`, `DispatcherQueue` and `ContentDialog`.
  - `Shell/WpfMainWindow.cs` and `Shell/WindowLayoutService.cs` (window placement) move to `AppWindow.Move`/`Resize`.
  - `WpfListDialogs`, `WpfOccurrenceDialogs`, `WpfExportDialogs` and `WpfImportDialogs` (WPF dialogs and file dialogs) move to `ContentDialog` and the WinUI file pickers, which an unpackaged app must initialize with the window handle.
  - `MainWindow` and the three views would be rewritten.
- **Unchanged:** `NamedPipeSingleInstance`, `RegistryRunKey`, `ExplorerFolderOpener` and `AppSdkNotificationService`, apart from their dispatcher calls.

## Decision

Stay on WPF. Do not port to WinUI 3 now, and do not pursue Native AOT while TaskDesk uses the current EF Core Data layer.

- **Native AOT** is the only route to a clearly faster start (134 ms shell) and lower memory (113 MB). It is blocked by EF Core and would require rewriting the Data layer's queries onto experimental EF Core features that failed in three different ways here.
- **WinUI without AOT** is not smaller than framework-dependent WPF (226 to 281 MB self-contained against 137.7 MB). It uses more memory than WPF (176 to 194 MB), and trimming it makes it slower. Its faster ReadyToRun start (841 ms) comes from a spike with a fraction of the app's start-up work, so it does not show that a full port would be faster.
- **The port** would rewrite the whole App project (tray, borderless quick-add, drag-drop, theme, hotkey, dialogs and every view) for a gain the measurements do not establish.

Cheaper levers, in order, each to be measured before it is adopted:

1. Find where WPF's 1.65 s goes. The spike suggests EF Core costs about 550 ms even with ReadyToRun.
2. Try an EF Core compiled model in the JIT build, which does not need AOT and was generated without errors here.
3. Shrink the Windows App SDK payload (#21).

Revisit WinUI + AOT if EF Core's query precompilation and NativeAOT support leave experimental status and handle this Data layer, or if the Data layer moves off EF Core for other reasons.

## Consequences

- The WPF-specific code listed above stays. `WpfThemeService` remains the only user of the experimental WPF0001 API.
- `spikes/WinUiAotSpike/` stays outside the solution as the reference for a future re-measurement. Its own `Directory.Build.props` and `Directory.Packages.props` keep the product gate unaffected.
- Not measured:
  - trimmed + ReadyToRun;
  - AOT with a Data layer rewritten for precompiled queries;
  - the tray menu, quick-add and the borderless window under trimming or AOT, beyond start-up;
  - behaviour on a machine without the Visual Studio C++ tools or Windows App Runtime packages.
