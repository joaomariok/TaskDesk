using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Shell;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The Smart Views in the main window's native navigation pane: their items, open-Task counts shown only above
/// zero, the red Overdue badge, selection, the Ctrl+1..5 accelerators, the rail and the remembered width.
/// </summary>
[TestClass]
public sealed class NavigationPaneTests
{
    private static readonly string[] SmartViewNames = ["Today", "Upcoming", "Overdue", "All", "Completed"];

    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task The_Smart_Views_are_items_of_a_native_navigation_pane_with_icons_and_their_automation_ids()
    {
        using var db = new TempDatabase();
        var window = new MainWindow(NewViewModel(db));
        try
        {
            window.Activate();
            var items = await SmartViewItemsAsync(window);

            CollectionAssert.AreEqual(SmartViewNames, items.Select(i => (string)i.Content).ToList());
            CollectionAssert.AreEqual(SmartViewNames, items.Select(AutomationProperties.GetName).ToList());
            CollectionAssert.AreEqual(
                SmartViewNames.Select(n => "SmartView" + n).ToList(),
                items.Select(AutomationProperties.GetAutomationId).ToList());
            Assert.IsTrue(items.All(i => i.Icon is FontIcon { Glyph.Length: > 0 }), "Every Smart View has an icon.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_count_shows_only_above_zero_and_shows_again_when_it_rises()
    {
        using var db = new TempDatabase();
        var viewModel = NewViewModel(db);
        viewModel.TodayCount = 0;
        viewModel.UpcomingCount = 3;
        viewModel.AllCount = 12;
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            var items = await SmartViewItemsAsync(window);
            InfoBadge? Badge(string name) => items.Single(i => (string)i.Content == name).InfoBadge;

            Assert.AreEqual(Visibility.Collapsed, Badge("Today")!.Visibility, "A count of 0 is not shown.");
            Assert.AreEqual(Visibility.Visible, Badge("Upcoming")!.Visibility);
            Assert.AreEqual(3, Badge("Upcoming")!.Value);
            Assert.AreEqual(12, Badge("All")!.Value);
            Assert.AreEqual(Visibility.Collapsed, Badge("Overdue")!.Visibility);
            Assert.IsNull(Badge("Completed"), "Completed has no count.");

            viewModel.TodayCount = 2;
            viewModel.OverdueCount = 1;

            Assert.AreEqual(Visibility.Visible, Badge("Today")!.Visibility);
            Assert.AreEqual(2, Badge("Today")!.Value);
            Assert.AreEqual(Visibility.Visible, Badge("Overdue")!.Visibility);
            Assert.AreEqual(1, Badge("Overdue")!.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task Overdue_has_a_red_badge_and_every_badge_reads_at_body_text_contrast(ElementTheme theme)
    {
        var ct = TestContext.CancellationToken;
        var colours = await ThemeColours.ResolveAsync(
            theme, ["OverdueBrush", "SolidBackgroundFillColorBaseBrush"], ct);
        using var db = new TempDatabase();
        var viewModel = NewViewModel(db);
        viewModel.TodayCount = 4;
        viewModel.OverdueCount = 2;
        var window = new MainWindow(viewModel);
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = theme;
            window.Activate();
            var items = await SmartViewItemsAsync(window);
            var overdue = items.Single(i => (string)i.Content == "Overdue").InfoBadge;
            var today = items.Single(i => (string)i.Content == "Today").InfoBadge;
            await UiWait.UntilAsync(() => overdue.IsLoaded && today.IsLoaded, () => "The badges never loaded.", ct);

            Assert.AreEqual(colours["OverdueBrush"], Colour(overdue.Background), $"{theme}: Overdue's badge is not red.");
            Assert.AreNotEqual(colours["OverdueBrush"], Colour(today.Background), $"{theme}: Today's badge is red too.");
            foreach (var badge in new[] { overdue, today })
            {
                var fill = ThemeColours.Over(Colour(badge.Background), colours["SolidBackgroundFillColorBaseBrush"]);
                var ink = ThemeColours.Over(Colour(badge.Foreground), fill);
                var contrast = ThemeColours.Contrast(ink, fill);
                Assert.IsGreaterThanOrEqualTo(4.5, contrast, $"{theme}: badge text contrast is {contrast:0.00}:1.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Selecting_a_Smart_View_in_the_pane_loads_its_Tasks()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Due today", completed: false);
        await db.AddTaskAsync(inbox, "No due", completed: false);
        var dueToday = (await db.QueryAsync(c => c.Tasks)).Single(t => t.Title == "Due today").Id;
        await db.Get<IDueService>().SetDueAsync(dueToday, new DateOnly(2026, 10, 16), null, ct);
        var viewModel = NewViewModelWithDue(db);
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            var items = await SmartViewItemsAsync(window);

            window.Navigation.Pane.SelectedItem = items.Single(i => (string)i.Content == "Today");

            await UiWait.UntilAsync(() => viewModel.CurrentView == SmartView.Today && Titles(viewModel).SequenceEqual(["Due today"]),
                () => $"The page shows {viewModel.CurrentView} with [{string.Join(", ", Titles(viewModel))}].", ct);
            await UiWait.UntilAsync(() => viewModel.TodayCount == 1 && viewModel.AllCount == 2,
                () => $"Counts were Today {viewModel.TodayCount}, All {viewModel.AllCount}.", ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Ctrl_1_to_Ctrl_5_are_the_Smart_Views_accelerators_and_switching_views_moves_the_selection()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var viewModel = NewViewModelWithDue(db);
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            var items = await SmartViewItemsAsync(window);

            for (var n = 0; n < items.Count; n++)
            {
                var accelerator = items[n].KeyboardAccelerators.Single();
                Assert.AreEqual(Windows.System.VirtualKey.Number1 + n, accelerator.Key, $"{SmartViewNames[n]}'s key.");
                Assert.AreEqual(Windows.System.VirtualKeyModifiers.Control, accelerator.Modifiers);
            }

            // What an accelerator runs: the view model's command, from anywhere in the window.
            foreach (var view in new[] { SmartView.Overdue, SmartView.Completed, SmartView.Today })
            {
                await viewModel.SelectSmartViewCommand.ExecuteAsync(view);
                Assert.AreEqual(view.ToString(), (string)((NavigationViewItem)window.Navigation.Pane.SelectedItem).Content);
            }

            await viewModel.ShowSmartViewAsync(null, ct);
            Assert.IsNull(window.Navigation.Pane.SelectedItem, "The List page selects no Smart View.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Below_900_px_the_pane_is_the_compact_rail_and_wider_it_is_the_full_sidebar_again()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var window = new MainWindow(NewViewModel(db));
        try
        {
            window.Activate();
            await SmartViewItemsAsync(window);
            var pane = window.Navigation.Pane;
            var splitter = VisualTree.Find<FrameworkElement>(window.Content, "SidebarSplitter");
            window.SetPaneWidths(LayoutWithSidebar(250));

            await ResizeClientAsync(window, 1200);
            Assert.IsFalse(window.Navigation.IsRail);
            Assert.AreEqual(NavigationViewPaneDisplayMode.Left, pane.PaneDisplayMode);
            Assert.IsTrue(pane.IsPaneOpen);
            Assert.IsFalse(pane.IsPaneToggleButtonVisible, "The full sidebar has no hamburger.");
            Assert.AreEqual(Visibility.Visible, splitter.Visibility);

            await ResizeClientAsync(window, 850);
            Assert.IsTrue(window.Navigation.IsRail, "850 px wide is the rail.");
            Assert.AreEqual(NavigationViewPaneDisplayMode.LeftCompact, pane.PaneDisplayMode);
            Assert.IsFalse(pane.IsPaneOpen);
            Assert.IsTrue(pane.IsPaneToggleButtonVisible, "The rail's hamburger opens the full pane as an overlay.");
            Assert.AreEqual(Visibility.Collapsed, splitter.Visibility, "The rail has no splitter.");
            CollectionAssert.AreEqual(SmartViewNames,
                (await SmartViewItemsAsync(window)).Select(AutomationProperties.GetName).ToList(), "Rail items keep their names.");
            Assert.AreEqual(250, window.MeasurePaneWidths()[PaneLayout.Navigation], "The rail keeps the full sidebar's width.");

            await ResizeClientAsync(window, 1000);
            Assert.IsFalse(window.Navigation.IsRail);
            Assert.AreEqual(NavigationViewPaneDisplayMode.Left, pane.PaneDisplayMode);
            Assert.IsTrue(pane.IsPaneOpen);
            Assert.AreEqual(250, pane.OpenPaneLength);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_sidebar_width_comes_from_the_saved_layout_within_its_limits_and_the_splitter_resizes_it()
    {
        using var db = new TempDatabase();
        var window = new MainWindow(NewViewModel(db));
        try
        {
            window.Activate();
            await SmartViewItemsAsync(window);
            var pane = window.Navigation.Pane;

            Assert.AreEqual(180, pane.OpenPaneLength, "The default width.");
            window.SetPaneWidths(LayoutWithSidebar(250));
            Assert.AreEqual(250, pane.OpenPaneLength);
            window.SetPaneWidths(LayoutWithSidebar(900));
            Assert.AreEqual(400, pane.OpenPaneLength, "Clamped to the maximum.");
            window.SetPaneWidths(null);
            Assert.AreEqual(180, pane.OpenPaneLength);

            var splitter = VisualTree.Find<CommunityToolkit.WinUI.Controls.PropertySizer>(window.Content, "SidebarSplitter");
            Assert.AreEqual(140, splitter.Minimum);
            Assert.AreEqual(400, splitter.Maximum);
            splitter.Binding = 300; // what a drag of the splitter sets
            Assert.AreEqual(300, pane.OpenPaneLength);
            Assert.AreEqual(300, window.MeasurePaneWidths()[PaneLayout.Navigation]);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_pane_keeps_the_automation_ids_and_names_the_UI_tests_use()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var window = new MainWindow(NewViewModel(db));
        try
        {
            window.Activate();
            await SmartViewItemsAsync(window);
            await ResizeClientAsync(window, 1200);
            FrameworkElement? WithId(string id) => VisualTree.Descendants(window.Content).OfType<FrameworkElement>()
                .FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);

            var navigation = WithId("NavigationPane");
            Assert.IsNotNull(navigation, "No element has the id NavigationPane.");
            Assert.IsNotNull(FrameworkElementAutomationPeer.CreatePeerForElement(navigation), "NavigationPane is not exposed to UI Automation.");
            Assert.AreEqual(window.Navigation.Pane.OpenPaneLength, navigation.ActualWidth, 1, "NavigationPane spans the sidebar's width.");

            var smartViews = WithId("SmartViewList");
            Assert.IsNotNull(smartViews, "No element has the id SmartViewList.");
            Assert.IsNotNull(FrameworkElementAutomationPeer.CreatePeerForElement(smartViews), "SmartViewList is not exposed to UI Automation.");
            Assert.AreEqual("Smart Views", AutomationProperties.GetName(smartViews));
            Assert.AreEqual(SmartViewNames.Length,
                VisualTree.Descendants(smartViews).OfType<NavigationViewItem>().Count(), "The Smart Views are inside SmartViewList.");

            var splitter = WithId("SidebarSplitter");
            Assert.IsNotNull(splitter);
            Assert.AreEqual("Resize sidebar", AutomationProperties.GetName(splitter));

            await ResizeClientAsync(window, 850);
            await UiWait.UntilAsync(() => WithId("SidebarExpandButton") is { Visibility: Visibility.Visible },
                () => "The rail has no SidebarExpandButton.", ct);
        }
        finally
        {
            window.Close();
        }
    }

    private static WindowLayout LayoutWithSidebar(double width) =>
        new(0, 0, 1200, 700, PaneWidths: new Dictionary<string, double> { [PaneLayout.Navigation] = width });

    // Resizes the window so its content is `width` device-independent pixels wide, and waits for the layout.
    private async Task ResizeClientAsync(MainWindow window, double width)
    {
        var content = (FrameworkElement)window.Content;
        var scale = content.XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(600 * scale)));
        await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - width) < 1,
            () => $"The content is {content.ActualWidth} px wide, not {width}.", TestContext.CancellationToken);
        content.UpdateLayout();
    }

    private static List<string> Titles(MainViewModel viewModel) =>
        [.. viewModel.OverdueTasks.Concat(viewModel.Tasks).Select(t => t.Title)];

    private static MainViewModel NewViewModelWithDue(TempDatabase db) =>
        new(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, db.Get<IDueService>(),
            db.Get<ITaskSearchService>(), listService: db.Get<IListService>());

    private static Windows.UI.Color Colour(Microsoft.UI.Xaml.Media.Brush brush) =>
        ((Microsoft.UI.Xaml.Media.SolidColorBrush)brush).Color;

    // A view model without a Due service keeps the counts a test sets: nothing recounts them.
    private static MainViewModel NewViewModel(TempDatabase db) =>
        new(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, listService: db.Get<IListService>());

    private async Task<List<NavigationViewItem>> SmartViewItemsAsync(MainWindow window)
    {
        List<NavigationViewItem> Items() => VisualTree.Descendants(window.Content)
            .OfType<NavigationViewItem>()
            .Where(i => AutomationProperties.GetAutomationId(i).StartsWith("SmartView", StringComparison.Ordinal))
            .ToList();
        await UiWait.UntilAsync(() => Items().Count == SmartViewNames.Length,
            () => $"Expected {SmartViewNames.Length} Smart View items, found {Items().Count}.", TestContext.CancellationToken);
        return Items();
    }
}
