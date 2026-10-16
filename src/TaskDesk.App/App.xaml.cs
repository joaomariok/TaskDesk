using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskDesk.Core.Paths;
using TaskDesk.Data;
using TaskDesk.Data.Logging;
using TaskDesk.Presentation;

namespace TaskDesk.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.Default();
        paths.EnsureDirectories();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddTaskDeskFileLogging(paths);
        builder.Services.AddTaskDeskData(paths);
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        _host = builder.Build();

        await _host.StartAsync();
        await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        await window.ViewModel.LoadAsync();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
