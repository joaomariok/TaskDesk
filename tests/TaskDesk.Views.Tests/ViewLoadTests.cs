using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Smoke-loads every window and view of TaskDesk.Views in a live window. XAML compiles even when it fails at load
/// time (a missing resource key, a wrong-type value, a template that cannot apply); these tests make the gate see
/// it, and the inventory makes sure a new window or view gets a row here.
/// </summary>
[TestClass]
public sealed class ViewLoadTests
{
    // Every Window and UserControl of TaskDesk.Views, built with the smallest view model it needs over a real
    // (empty) database, as the shell builds it.
    private static readonly Dictionary<string, Func<TempDatabase, FrameworkElementOrWindow>> Views = new()
    {
        [nameof(MainWindow)] = db =>
        {
            var viewModel = NewMainViewModel(db);
            return new MainWindow(viewModel, NewSidebarViewModel(viewModel, db));
        },
        [nameof(QuickAddWindow)] = db => new QuickAddWindow(NewQuickAddViewModel(db)),
        [nameof(SettingsWindow)] = _ => new SettingsWindow(NewSettingsViewModel()),
        [nameof(StartupFailureWindow)] = _ => new StartupFailureWindow(),
        [nameof(MainView)] = db => new MainView(NewMainViewModel(db)),
        [nameof(DetailDock)] = db => new DetailDock(new MainView(NewMainViewModel(db))),
        [nameof(DetailPane)] = db => new DetailPane(NewMainViewModel(db)),
        [nameof(NavigationPane)] = db =>
        {
            var viewModel = NewMainViewModel(db);
            return new NavigationPane(viewModel, new DialogService(() => null), NewSidebarViewModel(viewModel, db));
        },
        [nameof(TaskListView)] = db => new TaskListView(NewMainViewModel(db)),
        [nameof(TaskRow)] = _ => new TaskRow
        {
            Row = new TaskRowViewModel(new TaskItem { Id = Guid.NewGuid(), Title = "Water plants" }),
        },
        [nameof(QuickAddView)] = db => new QuickAddView(NewQuickAddViewModel(db)),
        [nameof(SettingsView)] = _ => new SettingsView(NewSettingsViewModel()),
    };

    public TestContext TestContext { get; set; } = null!;

    public static IEnumerable<object[]> ViewNames => Views.Keys.Order().Select(name => new object[] { name });

    [TestMethod]
    public void Every_window_and_view_has_a_load_test()
    {
        var views = typeof(MainWindow).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(Window)) || t.IsSubclassOf(typeof(UserControl))))
            .Select(t => t.Name)
            .Order()
            .ToList();

        CollectionAssert.AreEqual(views, Views.Keys.Order().ToList(),
            $"Windows and views: [{string.Join(", ", views)}]; load tests: [{string.Join(", ", Views.Keys.Order())}].");
    }

    [UITestMethod]
    [DynamicData(nameof(ViewNames))]
    public async Task Window_or_view_loads_and_lays_out(string name)
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var built = Views[name](db);
        var window = built.Window ?? new Window { Content = built.Element };
        try
        {
            window.Activate();
            var root = window.Content as FrameworkElement;
            Assert.IsNotNull(root, $"{name} shows no content.");
            await UiWait.UntilAsync(() => root.IsLoaded, () => $"{name} never loaded.", ct);
            root.UpdateLayout();

            Assert.IsGreaterThan(0d, root.ActualWidth, $"{name} was not laid out.");
            var missed = UntemplatedControls(root);
            Assert.IsEmpty(missed, $"{name}: controls whose template never applied: {string.Join(", ", missed)}.");
        }
        finally
        {
            window.Close();
        }
    }

    // Shown controls in the laid-out tree whose template never applied. Layout skips a collapsed element and
    // everything under it, so those are not counted.
    private static List<string> UntemplatedControls(DependencyObject root)
    {
        var missed = new List<string>();
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (node is UIElement { Visibility: Visibility.Collapsed })
            {
                continue;
            }

            var children = VisualTreeHelper.GetChildrenCount(node);
            if (node is Control { Template: not null } control && children == 0)
            {
                missed.Add($"{control.GetType().Name} '{control.Name}'");
            }

            for (var i = 0; i < children; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        return missed;
    }

    private static MainViewModel NewMainViewModel(TempDatabase db) => new(
        db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
        dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
        listService: db.Get<IListService>());

    private static SidebarViewModel NewSidebarViewModel(MainViewModel viewModel, TempDatabase db) =>
        new(viewModel, db.Get<IListService>(), new NoListDialogs());

    private static QuickAddViewModel NewQuickAddViewModel(TempDatabase db) =>
        new(db.Get<ITaskCreator>(), db.Get<IListService>());

    private static SettingsViewModel NewSettingsViewModel()
    {
        var store = new InMemorySettingsStore();
        return new SettingsViewModel(
            store,
            new AutoStartService(new InMemoryRunKey()),
            new ThemeService(),
            new RecordingFolderOpener(),
            AppPaths.ForRoot(Path.Combine(Path.GetTempPath(), "TaskDesk-views-tests")),
            exePath: @"C:\TaskDesk\TaskDesk.exe",
            version: "1.2.3",
            new HotkeyService(new NoHotkeys(), store));
    }

    /// <summary>A window, or an element to show alone in a window of its own.</summary>
    private sealed class FrameworkElementOrWindow
    {
        public Window? Window { get; private init; }

        public FrameworkElement? Element { get; private init; }

        public static implicit operator FrameworkElementOrWindow(Window window) => new() { Window = window };

        public static implicit operator FrameworkElementOrWindow(FrameworkElement element) => new() { Element = element };
    }

    private sealed class NoHotkeys : IHotkeyRegistrar
    {
        public event Action? Pressed
        {
            add { }
            remove { }
        }

        public bool TryRegister(Hotkey hotkey) => true;

        public void Unregister()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoListDialogs : IListDialogs
    {
        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(false);

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult<string?>(null);
    }
}
