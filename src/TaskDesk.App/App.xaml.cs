using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskDesk.App.Shell;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Paths;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Claim single-instance ownership before any expensive start-up work.
        var singleInstance = new NamedPipeSingleInstance();
        if (!singleInstance.TryAcquire())
        {
            await singleInstance.SignalExistingAsync(CancellationToken.None);
            singleInstance.Dispose();
            Shutdown();
            return;
        }

        var paths = AppPaths.Default();
        paths.EnsureDirectories();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddTaskDeskFileLogging(paths);
        builder.Services.AddTaskDeskData(paths);
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<IListDialogs, WpfListDialogs>();
        builder.Services.AddSingleton<IOccurrenceDialogs, WpfOccurrenceDialogs>();
        builder.Services.AddSingleton<ListsViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<WpfMainWindow>();
        builder.Services.AddSingleton<IMainWindow>(sp => sp.GetRequiredService<WpfMainWindow>());
        builder.Services.AddSingleton<IAppShutdown>(sp => sp.GetRequiredService<WpfMainWindow>());
        builder.Services.AddSingleton<ISingleInstance>(singleInstance);
        builder.Services.AddSingleton<ITrayIcon, HNotifyTrayIcon>();
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
            sp.GetRequiredService<IBackupService>()));
        _host = builder.Build();

        await _host.StartAsync();
        try
        {
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }
        catch (DatabaseStartupException ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\nNothing was deleted.\n\nLog folder: {ex.LogsDirectory}\nBackups folder: {ex.BackupsDirectory}",
                "TaskDesk", MessageBoxButton.OK, MessageBoxImage.Error);
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
        window.Closing += (_, args) =>
        {
            layout.Save(window);
            args.Cancel = _shell.OnWindowClosing();
        };
        _shell.SettingsRequested += () => Dispatcher.Invoke(ShowSettings);
        _shell.QuickAddRequested += () => Dispatcher.InvokeAsync(ShowQuickAddAsync);

        // A conflict is logged and shown in Settings; it never blocks start-up.
        _hotkeys = _host.Services.GetRequiredService<HotkeyService>();
        _hotkeys.Triggered += () => _ = ShowQuickAddAsync();
        _hotkeys.Start();

        var minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        await _shell.StartAsync(minimized, exePath);
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
        viewModel.TaskCreated += async () =>
        {
            // MainViewModel.LoadAsync does not raise OpenTaskCountsChanged, so reload the Lists
            // explicitly to refresh their open-Task counts.
            await main.ViewModel.LoadAsync();
            await main.Lists.LoadAsync();
        };
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

        _settingsWindow = new SettingsWindow(_host!.Services.GetRequiredService<SettingsViewModel>());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Let Windows log off or shut down instead of hiding the window to the tray.
        _shell?.Exit();
        base.OnSessionEnding(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _shell?.Dispose();

        if (_host is not null)
        {
            await _host.Services.GetRequiredService<MainViewModel>().FlushPendingSaveAsync();
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
