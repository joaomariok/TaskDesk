using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
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

namespace TaskDesk.App;

public partial class App : Application
{
    private IHost? _host;
    private ShellController? _shell;
    private SettingsWindow? _settingsWindow;
    private QuickAddWindow? _quickAddWindow;
    private HotkeyService? _hotkeys;
    private INotificationService? _notifications;
    private ReminderScheduler? _reminders;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF override with a void signature; start-up failures are not recoverable.")]
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mode = RuntimeMode.FromEnvironment();

        // Claim single-instance ownership before any expensive start-up work.
        var singleInstance = new NamedPipeSingleInstance();
        if (!singleInstance.TryAcquire())
        {
            // Forward --minimized and a Reminder notification click to the running instance (SPEC 7.1).
            var forwarded = SecondLaunchArguments.Build(
                e.Args.Contains(SecondLaunchArguments.MinimizedFlag, StringComparer.OrdinalIgnoreCase),
                mode.IsSandbox ? null : ReadLaunchActivation());
            await singleInstance.SignalExistingAsync(forwarded, CancellationToken.None);
            singleInstance.Dispose();
            Shutdown();
            return;
        }

        var paths = AppPaths.Default();
        paths.EnsureDirectories();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddTaskDeskFileLogging(paths);
        builder.Services.AddSingleton(mode);
        builder.Services.AddTaskDeskData(paths);
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<IListDialogs, WpfListDialogs>();
        builder.Services.AddSingleton<IExportDialogs, WpfExportDialogs>();
        builder.Services.AddSingleton<IImportDialogs, WpfImportDialogs>();
        builder.Services.AddSingleton<IOccurrenceDialogs, WpfOccurrenceDialogs>();
        builder.Services.AddSingleton<ListsViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<WpfMainWindow>();
        builder.Services.AddSingleton<IMainWindow>(sp => sp.GetRequiredService<WpfMainWindow>());
        builder.Services.AddSingleton<IAppShutdown>(sp => sp.GetRequiredService<WpfMainWindow>());
        builder.Services.AddSingleton<ISingleInstance>(singleInstance);
        builder.Services.AddSingleton<HNotifyTrayIcon>();
        builder.Services.AddSingleton<ITrayIcon>(sp => sp.GetRequiredService<HNotifyTrayIcon>());
        builder.Services.AddSingleton<TrayBalloonNotificationService>();
        if (mode.IsSandbox)
        {
            // No AppNotificationManager registration: balloons only, so nothing is left behind in the registry.
            builder.Services.AddSingleton<INotificationService>(
                sp => sp.GetRequiredService<TrayBalloonNotificationService>());
        }
        else
        {
            builder.Services.AddSingleton<AppSdkNotificationService>();
            builder.Services.AddSingleton(sp => new NotificationDelivery(
                sp.GetRequiredService<AppSdkNotificationService>(),
                sp.GetRequiredService<TrayBalloonNotificationService>(),
                sp.GetRequiredService<ILogger<NotificationDelivery>>()));
            builder.Services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationDelivery>());
        }

        builder.Services.AddSingleton<ReminderScheduler>();
        builder.Services.AddSingleton<ReminderActivationHandler>();
        builder.Services.AddSingleton<IRunKey, RegistryRunKey>();
        builder.Services.AddSingleton<AutoStartService>();
        builder.Services.AddSingleton<IShellSettingsStore>(_ => new JsonShellSettingsStore(paths.SettingsFile));
        builder.Services.AddSingleton<ShellController>();
        builder.Services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();
        builder.Services.AddSingleton<HotkeyService>();
        builder.Services.AddTransient<QuickAddViewModel>();
        builder.Services.AddSingleton<IThemeService, WpfThemeService>();
        builder.Services.AddSingleton<IFolderOpener, ExplorerFolderOpener>();
        builder.Services.AddSingleton<WindowLayoutService>();
        builder.Services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
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
            sp.GetRequiredService<IImportDialogs>()));
        _host = builder.Build();

        await _host.StartAsync();
        _host.Services.GetRequiredService<ILogger<App>>().LogInformation(
            "Runtime mode: {Mode} (data folder {DataDirectory})", mode, paths.DataDirectory);
        try
        {
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }
        catch (DatabaseStartupException ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\nNothing was deleted.\n\nLog folder: {ex.LogsDirectory}\nBackups folder: {ex.BackupsDirectory}",
                "TaskDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            await _host.StopAsync();
            Shutdown(1);
            return;
        }

        _host.Services.GetRequiredService<IThemeService>()
            .Apply(_host.Services.GetRequiredService<IShellSettingsStore>().Load().Theme);

        var window = _host.Services.GetRequiredService<MainWindow>();
        var layout = _host.Services.GetRequiredService<WindowLayoutService>();
        layout.Restore(window);
        window.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is false)
            {
                layout.Save(window);
            }
        };
        await window.ViewModel.LoadAsync();
        await window.Lists.LoadAsync();

        _shell = _host.Services.GetRequiredService<ShellController>();
        // Exit waits for these, in order, before it calls Shutdown: WPF ends the process soon after.
        _shell.AddExitStep(() => window.ViewModel.FlushPendingSaveAsync());
        var host = _host;
        _shell.AddExitStep(() => host.StopAsync());
        window.Closing += (_, args) =>
        {
            layout.Save(window);
            args.Cancel = _shell.OnWindowClosing();
        };
        _shell.SettingsRequested += () => Dispatcher.Invoke(ShowSettings);
        _shell.ActivationRequested += activation => _ = Dispatcher.InvokeAsync(() => HandleActivationAsync(activation));
        _shell.QuickAddRequested += () => _ = Dispatcher.InvokeAsync(ShowQuickAddAsync);

        // A conflict is logged and shown in Settings; it never blocks start-up.
        _hotkeys = _host.Services.GetRequiredService<HotkeyService>();
        _hotkeys.Triggered += () => _ = ShowQuickAddAsync();
        _hotkeys.Start();

        // Rich notifications, or the tray balloon fallback with a banner explaining why (SPEC 7.3).
        _notifications = _host.Services.GetRequiredService<INotificationService>();
        if (_notifications is NotificationDelivery delivery)
        {
            delivery.FellBack += reason =>
                _ = Dispatcher.InvokeAsync(() => window.ViewModel.ShowNotificationBanner(reason));
        }

        _notifications.Activated += activation => _ = Dispatcher.InvokeAsync(() => HandleActivationAsync(activation));
        _notifications.Register();
        var launch = _notifications.GetLaunchActivation();

        // Launched by "Mark done" or a Snooze button on a toast: act without opening the window.
        var minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)
            || launch?.Action is ReminderAction.MarkDone or ReminderAction.SnoozeShort or ReminderAction.SnoozeLong;
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        await _shell.StartAsync(minimized, exePath);
        if (launch is not null)
        {
            await HandleActivationAsync(launch);
        }

        // Checks on start, every 30 s, on every Task change and on resume from sleep (SPEC 7.4).
        _reminders = _host.Services.GetRequiredService<ReminderScheduler>();
        _ = _reminders.StartAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    // The Reminder notification click that launched this (second) process, read before any registration.
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

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ = _reminders?.RequestCheck();
        }
    }

    private async Task HandleActivationAsync(NotificationActivation activation)
    {
        try
        {
            await _host!.Services.GetRequiredService<ReminderActivationHandler>().HandleAsync(activation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _host!.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Reminder notification action failed");
        }
    }

    private async Task ShowQuickAddAsync()
    {
        if (_quickAddWindow is not null)
        {
            _quickAddWindow.Activate();
            return;
        }

        var viewModel = _host!.Services.GetRequiredService<QuickAddViewModel>();
        var main = _host.Services.GetRequiredService<MainWindow>();
#pragma warning disable VSTHRD101 // Action event; a failed reload surfaces on the dispatcher as before
        viewModel.TaskCreated += async () =>
        {
            // MainViewModel.LoadAsync does not raise OpenTaskCountsChanged, so reload the Lists
            // explicitly to refresh their open-Task counts.
            await main.ViewModel.LoadAsync();
            await main.Lists.LoadAsync();
        };
#pragma warning restore VSTHRD101
        await viewModel.LoadAsync();

        _quickAddWindow = new QuickAddWindow(viewModel);
        _quickAddWindow.Closed += (_, _) => _quickAddWindow = null;
        _quickAddWindow.Show();
        _quickAddWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var settings = _host!.Services.GetRequiredService<SettingsViewModel>();
        var main = _host.Services.GetRequiredService<MainWindow>();
        // Save the pending detail edit and close the pane first, so no stale edit lands on imported data.
        settings.BeforeImport = main.ViewModel.PrepareForImportAsync;
#pragma warning disable VSTHRD101 // Action event; a failed reload surfaces on the dispatcher as before
        settings.Imported += async () =>
        {
            // The import changed Lists, Tags and Tasks underneath the main window: reload both panes.
            await main.ViewModel.LoadAsync();
            await main.Lists.LoadAsync();
        };
#pragma warning restore VSTHRD101
        _settingsWindow = new SettingsWindow(settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Let Windows log off or shut down instead of hiding the window to the tray, but only after the
        // pending save has landed: keep the dispatcher pumping while the exit steps run.
        if (_shell is not null)
        {
            WaitPumping(_shell.ExitAsync());
        }

        base.OnSessionEnding(e);
    }

    // Waits on the UI thread without blocking it, so continuations that need the dispatcher still run.
    private static void WaitPumping(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
    }

    // Runs after the exit steps (ShellController.ExitAsync) have flushed the pending save and stopped the
    // host, so nothing here awaits: WPF would end the process at the first await.
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _reminders?.Dispose();
        try
        {
            _notifications?.Unregister();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _host?.Services.GetRequiredService<ILogger<App>>().LogWarning(ex, "Unregistering notifications failed");
        }

        _hotkeys?.Dispose();
        _shell?.Dispose();

        _host?.Dispose();

        base.OnExit(e);
    }
}
