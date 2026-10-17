using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TaskDesk.App.Shell;
using TaskDesk.Core.Paths;
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

    public App()
    {
        InitializeComponent();
        // The exit steps (flush, stop the host) run after the main window has closed; ShellController ends the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) =>
            _host?.Services.GetRequiredService<ILogger<App>>().LogCritical(e.Exception, "Unhandled UI exception");
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
            await singleInstance.SignalExistingAsync(SecondLaunchArguments.Build(minimizedFlag, null), CancellationToken.None);
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
        // Every confirmation and message is a content dialog on the main window; file pickers open on it too.
        builder.Services.AddSingleton(sp => new DialogService(() => sp.GetRequiredService<MainWindow>()));
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
        builder.Services.AddSingleton<ITrayIcon, WinUiTrayIcon>();
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
        _host = builder.Build();

        await _host.StartAsync();
        var logger = _host.Services.GetRequiredService<ILogger<App>>();
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
            singleInstance.Dispose();
            Environment.ExitCode = 1;
            Exit();
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TaskDesk.ico"));
        // The saved theme applies before the window first shows; later switches reach it live.
        var themes = _host.Services.GetRequiredService<ThemeService>();
        themes.Register(window);
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
        // Exit waits for these, in order, before it ends the app.
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

        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        await shell.StartAsync(minimizedFlag, exePath);
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
            _host!.Services.GetRequiredService<ILogger<App>>().LogError(ex, "The quick-add card could not open");
        }
        finally
        {
            _quickAddOpening = false;
        }
    }

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
