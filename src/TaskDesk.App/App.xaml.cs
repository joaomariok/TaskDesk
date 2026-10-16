using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskDesk.App.Shell;
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
        _host = builder.Build();

        await _host.StartAsync();
        await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        await window.ViewModel.LoadAsync();

        _shell = _host.Services.GetRequiredService<ShellController>();
        window.Closing += (_, args) => args.Cancel = _shell.OnWindowClosing();

        var minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        await _shell.StartAsync(minimized, exePath);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Let Windows log off or shut down instead of hiding the window to the tray.
        _shell?.Exit();
        base.OnSessionEnding(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
