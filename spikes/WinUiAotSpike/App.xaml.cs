using System.Diagnostics;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Logging;

namespace WinUiAotSpike;

/// <summary>
/// Spike start-up: the same data wiring as the WPF app (AddTaskDeskData, IDatabaseInitializer, ITaskService),
/// a tray icon from H.NotifyIcon.WinUI and one window. No app notification registration, no Run key, no
/// single-instance mutex. Refuses to start unless TASKDESK_DATA_DIR is set, so it never touches real data.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;
    private TaskbarIcon? _tray;
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => WriteFatal(e.Exception);
    }

    /// <summary>Records a start-up failure next to the log, so a measurement run that never shows a window explains why.</summary>
    private static void WriteFatal(Exception exception)
    {
        var logs = AppPaths.Default().LogsDirectory;
        Directory.CreateDirectory(logs);
        File.AppendAllText(Path.Combine(logs, "spike-fatal.txt"), exception + Environment.NewLine);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startedAt = Process.GetCurrentProcess().StartTime;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)))
        {
            // Spike safety: never open the real data folder.
            Exit();
            return;
        }

        var paths = AppPaths.Default();
        paths.EnsureDirectories();
        var arguments = Environment.GetCommandLineArgs();
        var minimized = arguments.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var noData = arguments.Contains("--no-data", StringComparer.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddTaskDeskFileLogging(paths));
        services.AddTaskDeskData(paths);
        _services = services.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation(
            "Spike start; runtime mode {Mode}, no-data {NoData}", RuntimeMode.FromEnvironment(), noData);

        ITaskSource source;
        if (noData)
        {
            source = new InMemoryTaskSource();
        }
        else
        {
            try
            {
                await _services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization failed");
                WriteFatal(ex);
                Exit();
                return;
            }

            source = new ProductTaskSource(_services.GetRequiredService<ITaskService>());
        }

        try
        {
            _window = new MainWindow(source, _services.GetRequiredService<ILogger<MainWindow>>());
            await _window.LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating the window or loading the Default List failed");
            WriteFatal(ex);
            Exit();
            return;
        }

        CreateTrayIcon();
        if (!minimized)
        {
            _window.Activate();
        }

        _logger.LogInformation(
            "Started in {Elapsed} ms (minimized: {Minimized})",
            (DateTime.Now - startedAt).TotalMilliseconds, minimized);
    }

    private void CreateTrayIcon()
    {
        var show = new MenuFlyoutItem { Text = "Show TaskDesk" };
        show.Click += (_, _) => _window?.ShowFromTray();
        var exit = new MenuFlyoutItem { Text = "Exit" };
        exit.Click += (_, _) => ExitApp();
        var menu = new MenuFlyout();
        menu.Items.Add(show);
        menu.Items.Add(exit);

        _tray = new TaskbarIcon
        {
            ToolTipText = "TaskDesk (WinUI spike)",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new ActionCommand(() => _window?.ShowFromTray()),
            NoLeftClickDelay = true,
        };
        _tray.ForceCreate(false);
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        _window?.CloseForExit();
        _services?.Dispose();
        Exit();
    }
}
