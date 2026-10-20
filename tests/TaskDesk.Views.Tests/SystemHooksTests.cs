using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.App.Notifications;
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
/// The WinUI shell's system hooks, on a hidden top-level window (a message-only window gets no session-end
/// or power broadcasts): Windows ending the session flushes the pending detail edit before the app answers,
/// and resume from sleep checks Reminders at once, which sandbox mode shows as a tray balloon. The shell
/// sources are linked into this test app.
/// </summary>
[TestClass]
public sealed partial class SystemHooksTests
{
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WmPowerBroadcast = 0x0218;
    private const nint PbtApmSuspend = 0x0004;
    private const nint PbtApmResumeAutomatic = 0x0012;
    private static readonly nint EndSessionLogoff = unchecked((nint)0x80000000L); // ENDSESSION_LOGOFF
    private const uint GaParent = 1;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public void The_system_window_is_a_hidden_top_level_window_so_session_end_reaches_it()
    {
        using var window = new MessageWindow(topLevel: true);

        Assert.AreEqual(GetDesktopWindow(), GetAncestor(window.Handle, GaParent),
            "Windows sends WM_QUERYENDSESSION and power broadcasts to top-level windows only.");
        Assert.IsFalse(IsWindowVisible(window.Handle));
    }

    [UITestMethod]
    public async Task Ending_the_session_saves_the_pending_title_edit_before_Windows_gets_its_answer()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book dentist");
        var (window, viewModel) = await OpenAsync(db);
        using var system = new MessageWindow(topLevel: true);
        using var hook = new SessionEndHook(system, viewModel.FlushPendingSaveAsync);
        try
        {
            await SelectAsync(window, "Book dentist");
            var pane = window.Details.Pane;
            await UiWait.UntilAsync(() => pane.ActualWidth > 0, () => "The pane did not show.", Ct);
            VisualTree.Find<TextBox>(pane, "DetailTitleBox").Text = "Book the dentist";
            // The save is debounced on the test's fake clock, which never moves: it stays pending.
            await UiWait.UntilAsync(() => viewModel.DetailTitle == "Book the dentist",
                () => "The title edit did not reach the view model.", Ct);
            Assert.AreEqual("Book dentist", (await db.QueryAsync(c => c.Tasks)).Single().Title);

            var answer = SendMessageW(system.Handle, WmQueryEndSession, 0, EndSessionLogoff);

            Assert.AreEqual(1, answer, "The app must let the session end.");
            Assert.IsTrue(viewModel.PendingSave.IsCompleted, "The save must land before the answer.");
            Assert.AreEqual("Book the dentist", (await db.QueryAsync(c => c.Tasks)).Single().Title);
            Assert.AreEqual(0, SendMessageW(system.Handle, WmEndSession, 1, EndSessionLogoff));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Resume_from_sleep_checks_Reminders_at_once_and_sandbox_mode_shows_a_due_one_as_a_balloon()
    {
        using var db = await DatabaseAsync();
        var task = await AddAsync(db, "Call the bank");
        await db.Get<IReminderService>().AddReminderAsync(
            task, ReminderValue.Fixed(db.Clock.GetUtcNow().UtcDateTime.AddMinutes(5)), Ct);
        var tray = new FakeBalloonTray();
        using var services = NotificationsFor(Sandbox, tray);
        var notifications = services.GetRequiredService<INotificationService>();
        notifications.Register();
        // Never started: while the PC slept, the 30-second timer did not run.
        using var scheduler = new ReminderScheduler(
            db.Get<IReminderService>(), notifications, db.Get<ITaskChangeNotifier>(), db.Clock);
        using var system = new MessageWindow(topLevel: true);
        using var hook = new PowerResumeHook(system, () => _ = scheduler.RequestCheck());
        db.Clock.Advance(TimeSpan.FromMinutes(10));

        SendMessageW(system.Handle, WmPowerBroadcast, PbtApmSuspend, 0);
        await scheduler.CurrentCheck;
        Assert.IsEmpty(tray.Balloons, "Going to sleep checks nothing.");

        SendMessageW(system.Handle, WmPowerBroadcast, PbtApmResumeAutomatic, 0);
        await scheduler.CurrentCheck;

        Assert.AreEqual("Call the bank", tray.Balloons.Single().Title);
    }

    [UITestMethod]
    public void Sandbox_mode_delivers_Reminders_through_the_tray_balloon_without_registering_toasts()
    {
        using var services = NotificationsFor(Sandbox, new FakeBalloonTray());

        Assert.IsInstanceOfType<TrayBalloonNotificationService>(services.GetRequiredService<INotificationService>());
    }

    [UITestMethod]
    public void The_normal_mode_delivers_toasts_and_falls_back_to_the_tray_balloon()
    {
        using var services = NotificationsFor(RuntimeMode.Normal, new FakeBalloonTray());

        Assert.IsInstanceOfType<NotificationDelivery>(services.GetRequiredService<INotificationService>());
    }

    private static readonly RuntimeMode Sandbox = new(ManagesAutoStart: false, RegistersNotifications: false, ManagesUpdates: false);

    internal static ServiceProvider NotificationsFor(RuntimeMode mode, IBalloonTray tray)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tray);
        services.AddTaskDeskNotifications(mode, isInstalled: false);
        return services.BuildServiceProvider();
    }

    // Selects a Task's row in the open Task list as a click would.
    private async Task SelectAsync(MainWindow window, string title)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        await UiWait.UntilAsync(
            () => VisualTree.Descendants(list).OfType<ListViewItem>().Any(i => AutomationProperties.GetName(i) == title),
            () => $"No row '{title}'.", Ct);
        list.SelectedItem = window.ViewModel.Tasks.Single(t => t.Title == title);
        window.Content.UpdateLayout();
    }

    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(TempDatabase db)
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
            return (window, viewModel);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    internal static async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync();
        return db;
    }

    internal static async Task<Guid> AddAsync(TempDatabase db, string title, bool completed = false)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        await db.AddTaskAsync(listId, title, completed);
        return (await db.QueryAsync(c => c.Tasks.Where(t => t.Title == title))).Single().Id;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hWnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);
}

/// <summary>Records the Reminder balloons the fallback shows, and clicks the last one on demand.</summary>
internal sealed class FakeBalloonTray : IBalloonTray
{
    private readonly Lock _gate = new();
    private readonly List<(string Title, string Message)> _balloons = [];

    public event Action? BalloonClicked;

    public IReadOnlyList<(string Title, string Message)> Balloons
    {
        get
        {
            lock (_gate)
            {
                return [.. _balloons];
            }
        }
    }

    public void ShowBalloon(string title, string message)
    {
        lock (_gate)
        {
            _balloons.Add((title, message));
        }
    }

    public void Click() => BalloonClicked?.Invoke();
}
