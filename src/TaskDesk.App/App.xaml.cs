using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TaskDesk.App.Notifications;
using TaskDesk.App.Shell;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Data;
using TaskDesk.Data.Logging;
using TaskDesk.Presentation;
using TaskDesk.Views;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.App;

/// <summary>
/// Start-up: single instance, Generic Host and services, database, main window at its saved layout, first
/// load, then the shell (tray, close behaviour, second launch). Sandbox mode when TASKDESK_DATA_DIR is set.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    // Resolved once, while the host is alive: the last exit part disposes the host, and an unhandled exception
    // after that must still reach this logger instead of throwing ObjectDisposedException from the provider.
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();
        // The exit parts (stop the window's view models, which saves a pending edit, then stop the host) run after the main window has closed; ShellController ends the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // Logged, then the app ends (Handled stays false): an unhandled UI exception ends the app. The log file
        // sink is unbuffered.
        UnhandledException += (_, e) => _logger?.LogCritical(e.Exception, "Unhandled UI exception");
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI override with a void signature; start-up failures are not recoverable.")]
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var minimizedFlag = arguments.Contains(SecondLaunchArguments.MinimizedFlag, StringComparer.OrdinalIgnoreCase);
        var mode = RuntimeMode.FromEnvironment();
        // The Reminder toast click that launched this process, read before registration for the launch gate (to
        // forward it, and for the "launched by a toast" condition). The launch's Mark done or Snooze reads it again
        // after registration: see ReadLaunchActivation.
        var gateActivation = mode.RegistersNotifications ? ReadLaunchActivation() : null;

        // Before any expensive start-up work: forward to a running copy and end, or apply an update downloaded by an
        // earlier run and restart (ADR 0005), or carry on owning the instance. A failed apply is logged once the
        // host's logger exists; start-up carries on.
        var singleInstance = new NamedPipeSingleInstance(
            SingleInstanceNames.For(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)));
        var appUpdates = new VelopackAppUpdates();
        var decision = await LaunchGate.DecideAsync(
            singleInstance, appUpdates, mode, arguments[1..], gateActivation, CancellationToken.None);
        if (decision.Outcome != LaunchOutcome.CarryOn)
        {
            // The gate released the guard after forwarding; a successful apply never returns here.
            if (decision.Outcome == LaunchOutcome.ApplyAndRestart)
            {
                singleInstance.Dispose();
            }

            Exit();
            return;
        }

        var paths = AppPaths.Default();
        paths.EnsureDirectories();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddTaskDeskFileLogging(paths);
        builder.Services.AddSingleton(mode);
        builder.Services.AddTaskDeskData(paths);
        // Every confirmation and message is a content dialog on the window in use (main or Settings); file pickers
        // open on it too.
        builder.Services.AddSingleton<ActiveWindow>();
        builder.Services.AddSingleton(sp => new DialogService(() => sp.GetRequiredService<ActiveWindow>().Current));
        builder.Services.AddSingleton<WinUiDialogs>();
        builder.Services.AddSingleton<IListDialogs>(sp => sp.GetRequiredService<WinUiDialogs>());
        builder.Services.AddSingleton<IExportDialogs>(sp => sp.GetRequiredService<WinUiDialogs>());
        builder.Services.AddSingleton<IImportDialogs>(sp => sp.GetRequiredService<WinUiDialogs>());
        builder.Services.AddSingleton<IOccurrenceDialogs>(sp => sp.GetRequiredService<WinUiDialogs>());
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SidebarViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<WinUiMainWindow>();
        builder.Services.AddSingleton<IMainWindow>(sp => sp.GetRequiredService<WinUiMainWindow>());
        builder.Services.AddSingleton<IAppShutdown>(sp => sp.GetRequiredService<WinUiMainWindow>());
        builder.Services.AddSingleton<ISingleInstance>(singleInstance);
        builder.Services.AddSingleton<WinUiTrayIcon>();
        builder.Services.AddSingleton<ITrayIcon>(sp => sp.GetRequiredService<WinUiTrayIcon>());
        builder.Services.AddSingleton<IBalloonTray>(sp => sp.GetRequiredService<WinUiTrayIcon>());
        // Toasts with the balloon fallback, or the balloon only in sandbox mode (no toast registration).
        builder.Services.AddTaskDeskNotifications(mode, VelopackInstall.IsInstalled);
        builder.Services.AddSingleton<ReminderScheduler>();
        builder.Services.AddSingleton<IAppUpdates>(appUpdates);
        builder.Services.AddSingleton<UpdateScheduler>();
        builder.Services.AddSingleton<ReminderActivationHandler>();
        builder.Services.AddSingleton(sp => new ReminderActivationRouter(
            sp.GetRequiredService<MainWindow>().DispatcherQueue,
            sp.GetRequiredService<ReminderActivationHandler>(),
            sp.GetRequiredService<ILogger<ReminderActivationRouter>>(),
            sp.GetRequiredService<ShellController>().RunActivationAsync));
        builder.Services.AddSingleton<IRunKey, RegistryRunKey>();
        builder.Services.AddSingleton<AutoStartService>();
        builder.Services.AddSingleton<IShellSettingsStore>(_ => new JsonShellSettingsStore(paths.SettingsFile));
        builder.Services.AddSingleton<ShellController>();
        builder.Services.AddSingleton<WindowLayoutService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
        builder.Services.AddSingleton<MessageWindow>();
        builder.Services.AddSingleton<IHotkeyRegistrar, WinUiHotkeyRegistrar>();
        builder.Services.AddSingleton<HotkeyService>();
        builder.Services.AddTransient<QuickAddViewModel>();
        builder.Services.AddSingleton<IFolderOpener, ExplorerFolderOpener>();
        builder.Services.AddSingleton<IClipboardService>(
            sp => new WinUiClipboardService(sp.GetRequiredService<ILogger<WinUiClipboardService>>()));
        builder.Services.AddTransient(sp => new SettingsViewModel(
            sp.GetRequiredService<IShellSettingsStore>(),
            sp.GetRequiredService<AutoStartService>(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IFolderOpener>(),
            sp.GetRequiredService<IAppPaths>(),
            Environment.ProcessPath ?? "",
            sp.GetRequiredService<IAppUpdates>().CurrentVersion,
            sp.GetRequiredService<HotkeyService>(),
            sp.GetRequiredService<IBackupService>(),
            sp.GetRequiredService<IExportService>(),
            sp.GetRequiredService<IExportDialogs>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IImportService>(),
            sp.GetRequiredService<IImportDialogs>(),
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<UpdateScheduler>(),
            sp.GetRequiredService<ShellController>().RestartToUpdateAsync));
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger = logger;
        await _host.StartAsync();
        logger.LogInformation("Runtime mode: {Mode} (data folder {DataDirectory})", mode, paths.DataDirectory);
        if (decision.ApplyFailure is not null)
        {
            logger.LogError(decision.ApplyFailure, "Applying the downloaded update at start failed; starting this version");
        }

        try
        {
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }
        catch (DatabaseStartupException ex)
        {
            logger.LogCritical(ex, "The database could not be opened; exiting");
            // No main window exists yet, so the explaining dialog gets a window of its own.
            await new StartupFailureWindow().ShowAsync(
                StartupFailureText.Describe(ex.Message, ex.LogsDirectory, ex.BackupsDirectory));
            await _host.StopAsync();
            _host.Dispose();
            singleInstance.Dispose();
            Environment.ExitCode = 1;
            Exit();
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico"));
        // The saved theme applies before the window first shows; later switches reach it live.
        var themes = _host.Services.GetRequiredService<ThemeService>();
        themes.Applied += NativeMenuTheme.Apply;
        themes.Register(window);
        _host.Services.GetRequiredService<ActiveWindow>().Register(window);
        themes.Apply(_host.Services.GetRequiredService<IShellSettingsStore>().Load().Theme);
        var layout = _host.Services.GetRequiredService<WindowLayoutService>();
        layout.Restore(window);
        window.AppWindow.Changed += (sender, change) =>
        {
            if (change.DidVisibilityChange && !sender.IsVisible)
            {
                layout.Save(window);
            }
        };
        await window.ViewModel.LoadAsync();
        await window.Lists!.LoadAsync();

        var shell = _host.Services.GetRequiredService<ShellController>();
        var host = _host;
        // Closing hides to the tray or exits, per the close-behaviour setting.
        window.AppWindow.Closing += (_, closing) =>
        {
            layout.Save(window);
            closing.Cancel = shell.OnWindowClosing();
        };

        // The tray's Quick add and the global hotkey open the quick-add card. A hotkey conflict is logged
        // (Settings shows it), never blocking start-up.
        shell.QuickAddRequested += () => window.DispatcherQueue.TryEnqueue(ShowQuickAdd);
        var hotkeys = _host.Services.GetRequiredService<HotkeyService>();
        hotkeys.Triggered += () => window.DispatcherQueue.TryEnqueue(ShowQuickAdd);
        hotkeys.Start();

        // The tray's Settings and Ctrl+comma open the Settings window.
        shell.SettingsRequested += () => window.DispatcherQueue.TryEnqueue(ShowSettings);
        window.ViewModel.SettingsRequested += (_, _) => ShowSettings();

        // Rich notifications, or the tray balloon fallback with a banner explaining why (SPEC 7.3). Every
        // Reminder activation (toast, balloon, a later launch from a toast) opens or acts on its Task.
        var notifications = _host.Services.GetRequiredService<INotificationService>();
        if (notifications is NotificationDelivery delivery)
        {
            delivery.FellBack += reason =>
                window.DispatcherQueue.TryEnqueue(() => window.ViewModel.ShowNotificationBanner(reason));
        }

        var activations = _host.Services.GetRequiredService<ReminderActivationRouter>();
        activations.Attach(notifications);
        shell.ActivationRequested += activations.Route;
        notifications.Register();
        // Read again after registration, which delivers a launching toast's arguments (#99).
        var launch = notifications.GetLaunchActivation();

        // Launched by "Mark done" or a Snooze button on a toast: act without opening the window.
        var minimized = minimizedFlag
            || launch?.Action is ReminderAction.MarkDone or ReminderAction.SnoozeShort or ReminderAction.SnoozeLong;
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

        var reminders = _host.Services.GetRequiredService<ReminderScheduler>();
        var updates = _host.Services.GetRequiredService<UpdateScheduler>();

        // Session end and resume from sleep reach top-level windows only, so they get a hidden one of their own. The
        // shell starts them once it holds the exit parts.
        MessageWindow? systemWindow = null;
        SessionEndHook? sessionEnd = null;
        PowerResumeHook? resume = null;

        // The shell runs the exit parts in this order, after the Reminder activations. The window closes only once
        // they have run, so it first takes no more input and stops both view models, which saves a pending detail
        // edit: a reload during the host stop would query a host being stopped. Between the hotkey and the host
        // disposal, the shell releases the tray and the single-instance guard; disposing the host closes the
        // database and the log file. The shell stops the update checks when the exit starts (the UpdateScheduler it
        // gets from the host); only Restart to update waits for a running download.
        var exitParts = new ShellExitParts
        {
            PrepareWindow = window.PrepareForExitAsync,
            StopHost = () => host.StopAsync(),
            StopSystemHooks = () =>
            {
                resume?.Dispose();
                sessionEnd?.Dispose();
                systemWindow?.Dispose();
                reminders.Dispose();
                try
                {
                    notifications.Unregister();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    logger.LogWarning(ex, "Unregistering notifications failed");
                }

                return Task.CompletedTask;
            },
            ReleaseHotkey = () =>
            {
                hotkeys.Dispose();
                return Task.CompletedTask;
            },
            DisposeHost = async () =>
            {
                if (host is IAsyncDisposable asyncHost)
                {
                    await asyncHost.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            },
        };

        // The launch's activation and the Reminder checks start only while no exit has started: the exit disposes
        // what they use, and it waits for the activation, so a Mark done or Snooze from the toast is kept. The
        // Reminder checks run on start, every 30 s, on every Task change and on resume from sleep (SPEC 7.4). An
        // installed copy checks for updates on start and every 6 hours, unless the runtime mode turns updates off.
        await shell.LaunchAsync(new ShellLaunch
        {
            Minimized = minimized,
            ExePath = exePath,
            ExitParts = exitParts,
            StartSystemHooks = () =>
            {
                systemWindow = new MessageWindow(topLevel: true);
                // Windows signing out or shutting down: exit now (save the pending edit, stop the host), then answer.
                sessionEnd = new SessionEndHook(systemWindow, shell.ExitAsync);
                resume = new PowerResumeHook(systemWindow, () => _ = reminders.RequestCheck());
            },
            Activation = launch is null ? null : exitStarted => activations.HandleAsync(launch, exitStarted),
            Running = () =>
            {
                _ = reminders.StartAsync();
                updates.Start();
            },
        });
    }

    // The Reminder toast click that launched this process. Windows App SDK 1.8 hands a toast's arguments to the
    // process it launched only through the COM activator that registration sets up, so this read, before
    // registration, can miss them; the launch's own read stays after registration (#99).
    private static NotificationActivation? ReadLaunchActivation()
    {
        try
        {
            return AppSdkNotificationService.ReadLaunchActivation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // no Windows App SDK runtime: there was no toast to forward
        }
    }

    private QuickAddWindow? _quickAddWindow;
    private bool _quickAddOpening;

    /// <summary>Opens the quick-add card, or brings the open one forward. A saved Task reloads the main window.</summary>
    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "UI entry point; failures are logged here.")]
    private async void ShowQuickAdd()
    {
        if (_quickAddWindow is not null)
        {
            _quickAddWindow.Activate();
            return;
        }

        if (_quickAddOpening)
        {
            return;
        }

        _quickAddOpening = true;
        try
        {
            var main = _host!.Services.GetRequiredService<MainWindow>();
            var viewModel = _host.Services.GetRequiredService<QuickAddViewModel>();
#pragma warning disable VSTHRD101 // Action event; a failed reload reaches the UnhandledException log
            // MainViewModel.LoadAsync does not raise OpenTaskCountsChanged, so the Lists' counts reload here.
            viewModel.TaskCreated += async () =>
            {
                await main.ViewModel.LoadAsync();
                await main.Lists!.LoadAsync();
            };
#pragma warning restore VSTHRD101
            await viewModel.LoadAsync();

            var card = new QuickAddWindow(viewModel);
            _host.Services.GetRequiredService<ThemeService>().Register(card);
            card.Closed += (_, _) => _quickAddWindow = null;
            _quickAddWindow = card;
            card.Activate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger?.LogError(ex, "The quick-add card could not open");
        }
        finally
        {
            _quickAddOpening = false;
        }
    }

    private SettingsWindow? _settingsWindow;

    /// <summary>Opens the Settings window, or brings the open one forward. An import reloads the main window.</summary>
    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            var main = _host!.Services.GetRequiredService<MainWindow>();
            var settings = _host.Services.GetRequiredService<SettingsViewModel>();
            // Save the pending detail edit and close the pane first, so no stale edit lands on imported data.
            settings.BeforeImport = main.ViewModel.PrepareForImportAsync;
#pragma warning disable VSTHRD101 // Action event; a failed reload reaches the UnhandledException log
            // The import changed Lists, Tags and Tasks underneath the main window: reload both panes.
            settings.Imported += async () =>
            {
                await main.ViewModel.LoadAsync();
                if (main.Lists is not null)
                {
                    await main.Lists.LoadAsync();
                }
            };
#pragma warning restore VSTHRD101

            var window = new SettingsWindow(settings);
            window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico"));
            _host.Services.GetRequiredService<ThemeService>().Register(window);
            // Its dialogs and file pickers open on it.
            _host.Services.GetRequiredService<ActiveWindow>().Register(window);
            window.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow = window;
            window.Activate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger?.LogError(ex, "The Settings window could not open");
        }
    }
}
