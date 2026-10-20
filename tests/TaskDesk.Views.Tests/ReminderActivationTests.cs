using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Shell;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// A Reminder activation opens the Task it names in the main window, whichever way it arrives: a click on the
/// sandbox-mode tray balloon, the toast that launched the app, or a later launch from a toast forwarded through
/// the single-instance pipe (which reaches the shell on a background thread).
/// </summary>
[TestClass]
public sealed class ReminderActivationTests
{
    private static readonly RuntimeMode Sandbox = new(ManagesAutoStart: false, RegistersNotifications: false, ManagesUpdates: false);

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Clicking_a_Reminder_balloon_opens_its_Task_in_the_detail_pane()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        await SystemHooksTests.AddAsync(db, "Book dentist");
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        var tray = new FakeBalloonTray();
        using var services = SystemHooksTests.NotificationsFor(Sandbox, tray);
        var notifications = services.GetRequiredService<INotificationService>();
        var (window, shellWindow, router) = await OpenAsync(db);
        try
        {
            router.Attach(notifications);
            notifications.Register();
            notifications.Show(new ReminderNotification(taskId, "Call the bank", "Inbox"));

            tray.Click();

            await AssertPaneShowsAsync(window, shellWindow, "Call the bank");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_toast_that_launched_the_app_opens_its_Task_even_outside_the_current_view()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        await SystemHooksTests.AddAsync(db, "Book dentist");
        var taskId = await SystemHooksTests.AddAsync(db, "Filed taxes", completed: true);
        var (window, shellWindow, router) = await OpenAsync(db);
        try
        {
            await router.HandleAsync(new NotificationActivation(taskId, ReminderAction.Open));

            await AssertPaneShowsAsync(window, shellWindow, "Filed taxes");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_later_launch_from_a_toast_reaches_the_running_instance_and_opens_its_Task()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        await SystemHooksTests.AddAsync(db, "Book dentist");
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        var names = SingleInstanceNames.For(db.Paths.DataDirectory);
        var (window, shellWindow, router) = await OpenAsync(db);
        var tray = new FakeTrayIcon();
        using var shell = new ShellController(
            tray, shellWindow, new NamedPipeSingleInstance(names),
            new JsonShellSettingsStore(Path.Combine(db.Paths.DataDirectory, "settings.json")),
            new AutoStartService(new FakeRunKey(), Sandbox), new FakeShutdown());
        shell.ActivationRequested += router.Route;
        try
        {
            Assert.IsTrue(await shell.StartAsync(minimized: true, exePath: "TaskDesk.App.exe", Ct));

            using (var later = new NamedPipeSingleInstance(names))
            {
                Assert.IsFalse(later.TryAcquire());
                await later.SignalExistingAsync(
                    SecondLaunchArguments.Build(false, new NotificationActivation(taskId, ReminderAction.Open)), Ct);
            }

            await AssertPaneShowsAsync(window, shellWindow, "Call the bank");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Mark_done_from_the_toast_that_launched_the_app_is_kept_when_the_exit_starts()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        var (window, shellWindow, router) = await OpenAsync(db);
        try
        {
            using var exitStarted = new CancellationTokenSource();
            await exitStarted.CancelAsync();

            await router.HandleAsync(new NotificationActivation(taskId, ReminderAction.MarkDone), exitStarted.Token);

            var task = (await db.QueryAsync(c => c.Tasks.Where(t => t.Id == taskId))).Single();
            Assert.IsTrue(task.IsCompleted, "The exit dropped the Mark done the toast asked for.");
            Assert.AreEqual(0, shellWindow.Shown);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Opening_a_toast_as_the_exit_starts_shows_nothing_and_logs_no_error()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        await SystemHooksTests.AddAsync(db, "Book dentist");
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        var log = new RecordingLogger<ReminderActivationRouter>();
        var (window, shellWindow, router) = await OpenAsync(db, log);
        try
        {
            window.ViewModel.SearchText = "dentist";
            await window.ViewModel.ApplySearchCommand.ExecuteAsync(null);
            using var exitStarted = new CancellationTokenSource();
            await exitStarted.CancelAsync();

            // A Task the search hides: opening it reloads the page, which the exit cancels.
            await router.HandleAsync(new NotificationActivation(taskId, ReminderAction.Open), exitStarted.Token);

            Assert.AreEqual(0, shellWindow.Shown, "The window came back during the exit.");
            CollectionAssert.DoesNotContain(log.Levels, LogLevel.Error, "The exit's cancellation was logged as an error.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_toast_clicked_in_the_running_app_after_the_exit_started_leaves_the_window_hidden()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        using var exitStarted = new CancellationTokenSource();
        await exitStarted.CancelAsync();
        var (window, shellWindow, router) = await OpenAsync(db, exitStarted: exitStarted.Token);
        try
        {
            router.Route(new NotificationActivation(taskId, ReminderAction.Open));

            // The Task is in the current view, so the activation selects it at once; it runs before this check.
            await UiWait.UntilAsync(() => window.ViewModel.DetailTask?.Title == "Call the bank",
                () => "The activation never ran.", Ct);
            Assert.AreEqual(0, shellWindow.Shown, "The window came back during the exit.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Mark_done_clicked_in_the_running_app_as_the_exit_starts_lands_before_the_exit_steps()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        using var shell = new ShellController(
            new FakeTrayIcon(), new FakeMainWindow(),
            new NamedPipeSingleInstance(SingleInstanceNames.For(db.Paths.DataDirectory)),
            new JsonShellSettingsStore(Path.Combine(db.Paths.DataDirectory, "settings.json")),
            new AutoStartService(new FakeRunKey(), Sandbox), new FakeShutdown());
        bool? doneAtFlush = null;
        Func<Task> noPart = () => Task.CompletedTask;
        // The first exit part: the flush, then the host stop and dispose, which the Mark done must not outlive.
        await shell.LaunchAsync(
            new ShellLaunch
            {
                Minimized = true,
                ExePath = "TaskDesk.App.exe",
                ExitParts = new ShellExitParts
                {
                    PrepareWindow = async () => doneAtFlush =
                        (await db.QueryAsync(c => c.Tasks.Where(t => t.Id == taskId))).Single().IsCompleted,
                    StopHost = noPart,
                    StopSystemHooks = noPart,
                    ReleaseHotkey = noPart,
                    DisposeHost = noPart,
                },
                StartSystemHooks = () => { },
                Running = () => { },
            },
            Ct);
        var (window, _, router) = await OpenAsync(db, shell: shell);
        try
        {
            // The click and the tray's Exit in the same turn of the UI thread.
            router.Route(new NotificationActivation(taskId, ReminderAction.MarkDone));
            await shell.ExitAsync();

            Assert.IsTrue(doneAtFlush, "The exit steps ran before the Mark done clicked as the exit started.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_exit_goes_on_after_a_routed_activation_throws_past_its_handler()
    {
        using var db = await SystemHooksTests.DatabaseAsync();
        await SystemHooksTests.AddAsync(db, "Book dentist");
        var taskId = await SystemHooksTests.AddAsync(db, "Call the bank");
        using var shell = new ShellController(
            new FakeTrayIcon(), new FakeMainWindow(),
            new NamedPipeSingleInstance(SingleInstanceNames.For(db.Paths.DataDirectory)),
            new JsonShellSettingsStore(Path.Combine(db.Paths.DataDirectory, "settings.json")),
            new AutoStartService(new FakeRunKey(), Sandbox), new FakeShutdown());
        // Logging the exit's cancellation throws, so the failure escapes the router's catch blocks.
        var (window, _, router) = await OpenAsync(db, new ThrowingLogger<ReminderActivationRouter>(), shell: shell);
        try
        {
            window.ViewModel.SearchText = "dentist";
            await window.ViewModel.ApplySearchCommand.ExecuteAsync(null);

            // A Task the search hides: opening it reloads the page, which the exit cancels.
            router.Route(new NotificationActivation(taskId, ReminderAction.Open));
            var exit = shell.ExitAsync();

            await UiWait.UntilAsync(() => exit.IsCompleted, () => "The exit still waits for the failed activation.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    private async Task AssertPaneShowsAsync(MainWindow window, FakeMainWindow shellWindow, string title)
    {
        var pane = window.Details.Pane;
        await UiWait.UntilAsync(
            () => window.ViewModel.IsDetailPaneOpen && pane.ActualWidth > 0
                && VisualTree.Find<TextBox>(pane, "DetailTitleBox").Text == title,
            () => $"The pane does not show '{title}' (shows '{window.ViewModel.DetailTask?.Title}').", Ct);
        await UiWait.UntilAsync(() => shellWindow.Shown > 0, () => "The main window was not brought forward.", Ct);
    }

    private async Task<(MainWindow Window, FakeMainWindow ShellWindow, ReminderActivationRouter Router)> OpenAsync(
        TempDatabase db,
        ILogger<ReminderActivationRouter>? logger = null,
        CancellationToken exitStarted = default,
        ShellController? shell = null)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<Core.Tags.ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<Core.Lists.IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.Content is FrameworkElement { IsLoaded: true },
                () => "The window did not load.", Ct);
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1200 * scale), (int)Math.Round(600 * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - 1200) < 1,
                () => $"The content is {content.ActualWidth} px wide.", Ct);
            await viewModel.LoadAsync(Ct);
            var shellWindow = new FakeMainWindow();
            var router = new ReminderActivationRouter(
                window.DispatcherQueue, new ReminderActivationHandler(viewModel, shellWindow),
                logger ?? NullLogger<ReminderActivationRouter>.Instance,
                shell is null ? work => work(exitStarted) : shell.RunActivationAsync);
            return (window, shellWindow, router);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private sealed class ThrowingLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("The log is gone.");
    }

    private sealed class FakeMainWindow : IMainWindow
    {
        public int Shown { get; private set; }

        public bool IsVisible => Shown > 0;

        public void ShowAndFocus() => Shown++;

        public void Hide()
        {
        }
    }

    private sealed class FakeTrayIcon : ITrayIcon
    {
#pragma warning disable CS0067 // The shell subscribes; these tests never click the tray.
        public event Action? LeftClicked;
        public event Action? OpenRequested;
        public event Action? QuickAddRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;
        public event Action? RestartToUpdateRequested;
#pragma warning restore CS0067

        public void Show()
        {
        }

        public void ShowHint(string message)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeRunKey : IRunKey
    {
        public string? GetValue(string name) => null;

        public void SetValue(string name, string value)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class FakeShutdown : IAppShutdown
    {
        public void Shutdown()
        {
        }
    }
}
