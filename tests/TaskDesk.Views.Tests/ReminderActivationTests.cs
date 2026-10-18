using Microsoft.Extensions.DependencyInjection;
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
    private static readonly RuntimeMode Sandbox = new(ManagesAutoStart: false, RegistersNotifications: false);

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
            Assert.IsTrue(await shell.StartAsync(minimized: true, exePath: "TaskDesk.App.WinUI.exe", Ct));

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
        TempDatabase db)
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
                NullLogger<ReminderActivationRouter>.Instance);
            return (window, shellWindow, router);
        }
        catch
        {
            window.Close();
            throw;
        }
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
