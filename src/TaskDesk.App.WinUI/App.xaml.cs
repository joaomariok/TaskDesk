using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// WinUI start-up, in the WPF app's order: single instance, Generic Host and services, database, main window
/// at its saved layout, first load, then the shell (tray, close behaviour, second launch). Until the swap
/// (#68) it runs in sandbox mode only: without TASKDESK_DATA_DIR it exits before touching any data.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    // Resolved once, while the host is alive: the last exit step disposes the host, and an unhandled exception
    // after that must still reach this logger instead of throwing ObjectDisposedException from the provider.
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();
        // The exit steps (flush, stop the host) run after the main window has closed; ShellController ends the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // Logged, then the app ends (Handled stays false): the WPF app has no DispatcherUnhandledException
        // handler either, so an unhandled UI exception ends it too. The log file sink is unbuffered.
        UnhandledException += (_, e) => _logger?.LogCritical(e.Exception, "Unhandled UI exception");
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WinUI override with a void signature; start-up failures are not recoverable.")]
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)))
        {
            // Sandbox only until the swap: never open the real data folder, Run key or notification registration.
            Exit();
            return;
        }

        var arguments = Environment.GetCommandLineArgs();
        var minimizedFlag = arguments.Contains(SecondLaunchArguments.MinimizedFlag, StringComparer.OrdinalIgnoreCase);
        var mode = RuntimeMode.FromEnvironment();

        // Claim single-instance ownership before any expensive start-up work.
        var singleInstance = new NamedPipeSingleInstance(
            SingleInstanceNames.For(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)));
        if (!singleInstance.TryAcquire())
        {
            // This launch has the foreground; let the running instance take it when it shows its window.
            AllowSetForegroundWindow(AsfwAny);
            // Forward --minimized and a Reminder toast click to the running instance (SPEC 7.1).
            var forwarded = SecondLaunchArguments.Build(
                minimizedFlag, mode.RegistersNotifications ? ReadLaunchActivation() : null);
            await singleInstance.SignalExistingAsync(forwarded, CancellationToken.None);
            singleInstance.Dispose();
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
        builder.Services.AddSingleton<ListsViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<WinUiMainWindow>();
        builder.Services.AddSingleton<IMainWindow>(sp => sp.GetRequiredService<WinUiMainWindow>());
        builder.Services.AddSingleton<IAppShutdown>(sp => sp.GetRequiredService<WinUiMainWindow>());
        builder.Services.AddSingleton<ISingleInstance>(singleInstance);
        builder.Services.AddSingleton<WinUiTrayIcon>();
        builder.Services.AddSingleton<ITrayIcon>(sp => sp.GetRequiredService<WinUiTrayIcon>());
        builder.Services.AddSingleton<IBalloonTray>(sp => sp.GetRequiredService<WinUiTrayIcon>());
        // Toasts with the balloon fallback, or the balloon only in sandbox mode (no toast registration).
        builder.Services.AddTaskDeskNotifications(mode);
        builder.Services.AddSingleton<ReminderScheduler>();
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
            AppVersion(),
            sp.GetRequiredService<HotkeyService>(),
            sp.GetRequiredService<IBackupService>(),
            sp.GetRequiredService<IExportService>(),
            sp.GetRequiredService<IExportDialogs>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IImportService>(),
            sp.GetRequiredService<IImportDialogs>(),
            sp.GetRequiredService<IClipboardService>()));
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger = logger;
        await _host.StartAsync();
        logger.LogInformation("Runtime mode: {Mode} (data folder {DataDirectory})", mode, paths.DataDirectory);
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
        // Exit waits for these, in order, before it ends the app. The window closes only once they have run, so it
        // first takes no more input and its search ends: a reload during the flush or the host stop would query a
        // host being stopped.
        shell.AddExitStep(window.PrepareForExitAsync);
        shell.AddExitStep(() => window.ViewModel.FlushPendingSaveAsync());
        var host = _host;
        shell.AddExitStep(() => host.StopAsync());
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
        var launch = notifications.GetLaunchActivation();

        // Launched by "Mark done" or a Snooze button on a toast: act without opening the window.
        var minimized = minimizedFlag
            || launch?.Action is ReminderAction.MarkDone or ReminderAction.SnoozeShort or ReminderAction.SnoozeLong;
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

        // Every exit step is added here, with no await since the first: from shell.StartAsync on, the tray's
        // Exit and session end can start the exit, and ShellController rejects a step added after that.
        var reminders = _host.Services.GetRequiredService<ReminderScheduler>();
        StartSystemHooks(shell, reminders, notifications);

        // The last exit step, as the WPF app's OnExit: release the global hotkey, then the shell (tray,
        // single-instance guard) and the host, which closes the database and the log file.
        shell.AddExitStep(async () =>
        {
            hotkeys.Dispose();
            shell.Dispose();
            if (host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        });

        // The launch's activation and the Reminder checks start only while no exit has started: the exit disposes
        // what they use, and it waits for the activation, so a Mark done or Snooze from the toast is kept. The
        // Reminder checks run on start, every 30 s, on every Task change and on resume from sleep (SPEC 7.4).
        await shell.LaunchAsync(
            minimized,
            exePath,
            launch is null ? null : exitStarted => activations.HandleAsync(launch, exitStarted),
            () => _ = reminders.StartAsync());
    }

    // Session end and resume from sleep reach top-level windows only, so they get a hidden one of their own.
    private void StartSystemHooks(ShellController shell, ReminderScheduler reminders, INotificationService notifications)
    {
        var systemWindow = new MessageWindow(topLevel: true);
        // Windows signing out or shutting down: exit now (flush the pending edit, stop the host), then answer.
        var sessionEnd = new SessionEndHook(systemWindow, shell.ExitAsync);
        var resume = new PowerResumeHook(systemWindow, () => _ = reminders.RequestCheck());
        var logger = _host!.Services.GetRequiredService<ILogger<App>>();
        shell.AddExitStep(() =>
        {
            resume.Dispose();
            sessionEnd.Dispose();
            systemWindow.Dispose();
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
        });
    }

    // The Reminder toast click that launched this (second) process, read before any registration.
    private static NotificationActivation? ReadLaunchActivation()
    {
        try
        {
            return new AppSdkNotificationService(NullLogger<AppSdkNotificationService>.Instance).GetLaunchActivation();
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

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
