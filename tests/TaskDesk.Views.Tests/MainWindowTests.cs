using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Graphics;
using WinRT.Interop;

namespace TaskDesk.Views.Tests;

[TestClass]
public sealed partial class MainWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_main_window_shows_the_Tasks_of_the_database_as_rows_named_by_their_titles()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Book flights", completed: false);
        await db.AddTaskAsync(inbox, "Call the venue", completed: false);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
            List<ListViewItem> Rows() => VisualTree.Descendants(list).OfType<ListViewItem>().ToList();
            await UiWait.UntilAsync(() => Rows().Count == 2,
                () => $"Expected 2 rows, found {Rows().Count} ({viewModel.Tasks.Count} Tasks loaded).", ct);
            var rows = Rows();
            CollectionAssert.AreEqual(
                new[] { "Book flights", "Call the venue" },
                rows.Select(r => VisualTree.Descendants(r).OfType<TextBlock>().First().Text).ToList());
            CollectionAssert.AreEqual(
                new[] { "Book flights", "Call the venue" },
                rows.Select(AutomationProperties.GetName).ToList());
            Assert.AreEqual("OpenTaskList", AutomationProperties.GetAutomationId(list));
            Assert.AreEqual("TaskDesk", window.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public void The_main_window_opens_at_900_by_600_and_keeps_at_least_800_by_500_as_the_WPF_window_does()
    {
        using var db = new TempDatabase();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            var scale = GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;
            Assert.AreEqual(WindowLayoutCalculator.ToPhysical(900, scale), window.AppWindow.Size.Width);
            Assert.AreEqual(WindowLayoutCalculator.ToPhysical(600, scale), window.AppWindow.Size.Height);
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
            Assert.AreEqual(WindowLayoutCalculator.ToPhysical(800, scale), presenter.PreferredMinimumWidth);
            Assert.AreEqual(WindowLayoutCalculator.ToPhysical(500, scale), presenter.PreferredMinimumHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_window_width_with_its_frame_decides_the_rail_and_the_docked_detail_pane_as_in_WPF()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", ct);
            var scale = GetDpiForWindow(WindowNative.GetWindowHandle(window)) / 96.0;

            // The default 900 px window, whose content is narrower than 900 px, shows the full sidebar.
            Assert.IsTrue(((FrameworkElement)window.Content).ActualWidth < 900);
            Assert.IsFalse(window.Navigation.IsRail, "The default 900 px window shows the rail.");
            Assert.IsFalse(viewModel.IsDetailDocked);

            foreach (var (width, rail, docked) in new[] { (899, true, false), (1100, false, false), (1101, false, true), (900, false, false) })
            {
                window.AppWindow.Resize(new SizeInt32(
                    WindowLayoutCalculator.ToPhysical(width, scale), WindowLayoutCalculator.ToPhysical(600, scale)));
                await UiWait.UntilAsync(() => window.Navigation.IsRail == rail && viewModel.IsDetailDocked == docked,
                    () => $"At {width} px: rail {window.Navigation.IsRail}, docked {viewModel.IsDetailDocked}.", ct);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public void The_main_window_has_Mica_behind_the_content_and_the_standard_title_bar()
    {
        using var db = new TempDatabase();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            Assert.IsInstanceOfType<MicaBackdrop>(window.SystemBackdrop);
            Assert.IsFalse(window.ExtendsContentIntoTitleBar, "The window keeps the standard title bar.");
            Assert.IsNull(window.View.Background, "The view draws no background over the Mica.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Quick_add_has_keyboard_focus_when_the_main_window_opens()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);

            var quickAdd = VisualTree.Find<TextBox>(window.View, "QuickAddBox");
            await UiWait.UntilAsync(
                () => quickAdd.XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), quickAdd),
                () => "Quick add did not take keyboard focus.", ct);
            Assert.AreEqual("QuickAddBox", AutomationProperties.GetAutomationId(quickAdd));
            Assert.AreEqual("Quick add", AutomationProperties.GetName(quickAdd));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public Task Closing_the_main_window_stops_its_view_models_and_the_sidebar_Lists()
        => AssertStopsBothAsync(window =>
        {
            window.Close();
            return Task.CompletedTask;
        });

    [UITestMethod]
    public Task Preparing_the_main_window_for_the_exit_stops_its_view_models_and_the_sidebar_Lists()
        => AssertStopsBothAsync(window => window.PrepareForExitAsync());

    // Opens the window with its sidebar Lists, stops it the given way, then adds a Task and a List to the database:
    // a stopped view model loads neither.
    private async Task AssertStopsBothAsync(Func<MainWindow, Task> stop)
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());
        var lists = new ListsViewModel(db.Get<IListService>(), new NoListDialogs());

        var window = new MainWindow(viewModel, lists);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            await lists.LoadAsync(ct);
            var shownLists = lists.Lists.Select(l => l.Name).ToList();

            await stop(window);
            await db.AddTaskAsync(inbox, "Book flights", completed: false);
            await db.Get<IListService>().CreateListAsync("Work", ct);
            await viewModel.ApplySearchCommand.ExecuteAsync(null);
            await lists.LoadAsync(ct);

            Assert.AreEqual(0, viewModel.Tasks.Count, "The main view model reloaded after the stop.");
            CollectionAssert.AreEqual(
                shownLists, lists.Lists.Select(l => l.Name).ToList(), "The sidebar Lists reloaded after the stop.");
        }
        finally
        {
            window.Close();
        }
    }

    // The sidebar asks nothing in these tests.
    private sealed class NoListDialogs : IListDialogs
    {
        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(false);

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult<string?>(null);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
