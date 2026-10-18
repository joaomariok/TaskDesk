using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.Foundation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The header of the list area over a real database: the page heading and its command bar (Filter and the
/// List settings overflow), the Filter button and
/// Filter bar, search (Ctrl+F, Esc), quick add (Enter, Ctrl+N), the empty state and "Load more completed".
/// </summary>
[TestClass]
public sealed class ListHeaderTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task The_page_heading_names_the_page_and_reads_the_same_to_a_screen_reader()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var heading = VisualTree.Find<TextBlock>(window.View, "PageHeading");
            Assert.AreEqual("PageHeading", AutomationProperties.GetAutomationId(heading));

            foreach (var (view, name) in new (SmartView?, string)[]
                     {
                         (null, "Inbox"), (SmartView.Today, "Today"), (SmartView.Overdue, "Overdue"),
                         (SmartView.Completed, "Completed"),
                     })
            {
                await viewModel.ShowSmartViewAsync(view, Ct);
                await UiWait.UntilAsync(() => heading.Text == name, () => $"The heading read '{heading.Text}'.", Ct);
                Assert.AreEqual(name, FrameworkElementAutomationPeer.CreatePeerForElement(heading).GetName());
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_heading_overflow_shows_on_a_List_page_and_renames_and_colours_the_List()
    {
        using var db = await DatabaseAsync();
        var home = await db.Get<ITagService>().CreateTagAsync("home", Ct);
        var (window, viewModel) = await OpenAsync(db);
        var dialogs = new RenamingDialogs("Home");
        var lists = new ListsViewModel(db.Get<IListService>(), dialogs);
        try
        {
            await lists.LoadAsync(Ct);
            window.View.Lists = lists;
            await viewModel.LoadAsync(Ct);
            var bar = VisualTree.Find<CommandBar>(window.View, "HeadingCommands");
            Assert.AreEqual(CommandBarDefaultLabelPosition.Right, bar.DefaultLabelPosition);
            var more = await WaitForAsync(
                () => VisualTree.Descendants(bar).OfType<Button>()
                    .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "ListMenuButton"),
                "The command bar has no overflow button with the id ListMenuButton.");
            Assert.AreEqual("List settings", FrameworkElementAutomationPeer.CreatePeerForElement(more).GetName());
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Visible,
                () => "A List page has no List settings overflow.", Ct);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(more)).Invoke();
            await UiWait.UntilAsync(() => bar.IsOpen, () => "List settings did not open the overflow.", Ct);
            bar.IsOpen = false;
            await UiWait.UntilAsync(() => !bar.IsOpen, () => "The overflow did not close.", Ct);
            Assert.AreEqual("List settings", FrameworkElementAutomationPeer.CreatePeerForElement(more).GetName(),
                "Opening the overflow renamed it.");
            Assert.IsTrue(more.IsTabStop, "Tab does not reach List settings.");

            var commands = bar.SecondaryCommands.Cast<AppBarButton>().ToList();
            CollectionAssert.AreEqual(new[] { "Rename", "Colour" }, commands.Select(c => c.Label).ToList());
            var colours = ((MenuFlyout)commands[1].Flyout).Items.Cast<MenuFlyoutItem>().ToList();
            CollectionAssert.AreEqual(
                new[] { "None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple" },
                colours.Select(i => i.Text).ToList());

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(commands[0])).Invoke();
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.IsDefault).Name == "Home",
                () => "Rename did not rename the shown List.", Ct);

            Invoke(colours.Single(i => i.Text == "Green"));
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.IsDefault).ColorHex == "#107C10",
                () => "Colour did not colour the shown List.", Ct);

            Invoke(colours.Single(i => i.Text == "None"));
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.IsDefault).ColorHex is null,
                () => "None did not remove the shown List's colour.", Ct);

            var work = await db.Get<IListService>().CreateListAsync("Work", Ct);
            await lists.LoadAsync(Ct);
            await viewModel.ShowListAsync(work.Id, Ct);
            Invoke(colours.Single(i => i.Text == "Green"));
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.Name == "Work").ColorHex == "#107C10",
                () => "Colour did not colour Work, the shown List.", Ct);
            Assert.IsNull(lists.Lists.Single(l => l.IsDefault).ColorHex, "Colour also changed the Default List.");

            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Collapsed,
                () => "A Smart View shows the List settings overflow.", Ct);

            await viewModel.ShowListAsync(work.Id, Ct);
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Visible,
                () => "Going back to a List page did not show the List settings overflow.", Ct);
            await viewModel.ShowAsync(new TaskListPage.TagPage(home), Ct);
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Collapsed,
                () => "A Tag's page shows the List settings overflow.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Filter_button_shows_and_hides_the_Filter_bar_and_stays_pressed_while_a_Filter_is_on()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var filter = VisualTree.Find<AppBarToggleButton>(window.View, "FilterButton");
            var bar = VisualTree.Find<FrameworkElement>(window.View, "FilterBar");
            Assert.AreEqual("Filter", AutomationProperties.GetName(filter));
            Assert.AreEqual("Filter", filter.Label);
            Assert.IsInstanceOfType<FontIcon>(filter.Icon);
            Assert.IsTrue(filter.IsTabStop, "Tab does not reach Filter.");
            CollectionAssert.Contains(
                VisualTree.Find<CommandBar>(window.View, "HeadingCommands").PrimaryCommands.ToList(), filter,
                "Filter is not a command of the heading's command bar.");
            Assert.AreEqual("FilterBar", AutomationProperties.GetAutomationId(bar));
            Assert.AreEqual("Filters", AutomationProperties.GetName(bar));
            Assert.AreEqual(Visibility.Collapsed, bar.Visibility);

            Toggle(filter);
            Assert.AreEqual(Visibility.Visible, bar.Visibility, "Filter did not show the Filter bar.");
            Assert.IsTrue(filter.IsChecked, "Filter is not pressed while the Filter bar shows.");
            Toggle(filter);
            Assert.AreEqual(Visibility.Collapsed, bar.Visibility, "Filter again did not hide the Filter bar.");
            Assert.IsFalse(filter.IsChecked, "Filter still looks pressed with the Filter bar hidden.");

            Toggle(filter);
            var hasDue = FindByName<ToggleButton>(window.View, "Has due filter");
            Toggle(hasDue);
            await UiWait.UntilAsync(() => hasDue.IsChecked == true, () => "Has due did not turn on.", Ct);
            var clear = FindByName<Button>(window.View, "Clear filters");
            Assert.AreEqual(Visibility.Visible, clear.Visibility, "Clear filters is hidden while a Filter is on.");
            Toggle(filter);
            Assert.AreEqual(Visibility.Visible, bar.Visibility, "The Filter bar hid while a Filter is on.");
            Assert.IsTrue(filter.IsChecked, "Filter was released while a Filter is on.");
        }
        finally
        {
            window.Close();
        }
    }

    // At the narrowest window (800 px with its frame) the heading row keeps the 32 px of a text box: the compact
    // command bar sits right after the heading, and the search box, as tall as quick add, is centred in the row.
    [UITestMethod]
    public async Task The_heading_row_is_32_px_with_its_commands_right_after_the_heading_and_search_centred()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)Math.Round(800 * scale), (int)Math.Round(500 * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - content.XamlRoot.Size.Width) < 1
                    && window.AppWindow.Size.Width == (int)Math.Round(800 * scale),
                () => $"The window is {window.AppWindow.Size.Width} px wide.", Ct);
            await viewModel.LoadAsync(Ct);
            var bar = VisualTree.Find<CommandBar>(window.View, "HeadingCommands");
            var more = await WaitForAsync(
                () => VisualTree.Descendants(bar).OfType<Button>()
                    .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "ListMenuButton" && b.ActualWidth > 0),
                "The List settings overflow does not show on the Default List's page.");
            window.View.UpdateLayout();
            var heading = VisualTree.Find<TextBlock>(window.View, "PageHeading");
            var filter = VisualTree.Find<AppBarToggleButton>(window.View, "FilterButton");
            var search = VisualTree.Find<TextBox>(window.View, "SearchBox");
            var quickAdd = VisualTree.Find<TextBox>(window.View, "QuickAddBox");
            var row = (FrameworkElement)VisualTreeHelper.GetParent(search);
            Rect Bounds(FrameworkElement e) =>
                e.TransformToVisual(window.View).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

            Assert.IsLessThanOrEqualTo(32.5, row.ActualHeight, $"The heading row is {row.ActualHeight} px tall.");
            Assert.IsLessThanOrEqualTo(32.5, bar.ActualHeight, $"The command bar is {bar.ActualHeight} px tall.");
            Assert.IsLessThanOrEqualTo(32.5, filter.ActualHeight, $"Filter is {filter.ActualHeight} px tall.");
            Assert.IsLessThanOrEqualTo(32.5, more.ActualHeight, $"List settings is {more.ActualHeight} px tall.");

            var label = VisualTree.Descendants(filter).OfType<TextBlock>().Single(t => t.Name == "TextLabel");
            var icon = VisualTree.Descendants(filter).OfType<FrameworkElement>().Single(e => e.Name == "ContentViewbox");
            Assert.IsGreaterThanOrEqualTo(Bounds(icon).Right - 0.5, Bounds(label).Left, "Filter's label is not right of its icon.");
            Assert.IsGreaterThanOrEqualTo(Bounds(filter).Top - 0.5, Bounds(label).Top, "Filter's label is cut at the top.");
            Assert.IsLessThanOrEqualTo(Bounds(filter).Bottom + 0.5, Bounds(label).Bottom, "Filter's label is cut at the bottom.");

            Assert.AreEqual(quickAdd.ActualHeight, search.ActualHeight, 0.5, "Search is not as tall as quick add.");
            var rowBounds = Bounds(row);
            var searchBounds = Bounds(search);
            Assert.AreEqual(rowBounds.Top + rowBounds.Height / 2, searchBounds.Top + searchBounds.Height / 2, 0.5,
                "Search is not centred in the heading row.");

            var headingRight = Bounds(heading).Right;
            var barBounds = Bounds(bar);
            Assert.IsGreaterThanOrEqualTo(headingRight - 0.5, barBounds.Left, "The command bar starts over the heading.");
            Assert.IsLessThanOrEqualTo(headingRight + 12, barBounds.Left,
                $"The command bar starts at {barBounds.Left}, not right after the heading ending at {headingRight}.");
            Assert.IsLessThanOrEqualTo(searchBounds.Left + 0.5, Bounds(more).Right, "List settings runs into search.");
            Assert.IsLessThanOrEqualTo(searchBounds.Left + 0.5, Bounds(filter).Right, "Filter runs into search.");
            Assert.IsLessThanOrEqualTo(window.View.ActualWidth + 0.5, searchBounds.Right, "Search runs past the page.");
            Assert.AreEqual(viewModel.PageHeading, heading.Text);
            Assert.IsFalse(heading.IsTextTrimmed, "The heading is trimmed in the narrowest window.");
        }
        finally
        {
            window.Close();
        }
    }

    // Fourteen Tags in a narrow window cannot share one line with the other Filters: like the WPF app, the bar wraps them.
    [UITestMethod]
    public async Task Tag_chips_that_do_not_fit_on_one_line_wrap_inside_the_Filter_bar()
    {
        using var db = await DatabaseAsync();
        string[] names =
        [
            "call", "errand", "family", "finance", "garden", "health", "home", "idea", "money", "read", "travel",
            "urgent", "waiting", "work",
        ];
        foreach (var name in names)
        {
            await db.Get<ITagService>().CreateTagAsync(name, Ct);
        }

        var (window, viewModel) = await OpenAsync(db);
        try
        {
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(800 * scale), (int)Math.Round(600 * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - 800) < 1,
                () => $"The content is {content.ActualWidth} px wide, not 800.", Ct);
            await viewModel.LoadAsync(Ct);
            viewModel.ToggleFilterBarCommand.Execute(null);
            var bar = VisualTree.Find<FrameworkElement>(window.View, "FilterBar");
            List<ToggleButton> Chips() =>
            [
                .. VisualTree.Descendants(bar).OfType<ToggleButton>()
                    .Where(b => AutomationProperties.GetName(b).StartsWith('#') && b.ActualWidth > 0),
            ];
            await UiWait.UntilAsync(() => Chips().Count == names.Length,
                () => $"{Chips().Count} of {names.Length} Tag chips show.", Ct);
            window.View.UpdateLayout();

            var barRight = bar.TransformToVisual(window.View).TransformPoint(new Point(bar.ActualWidth, 0)).X;
            Assert.IsLessThanOrEqualTo(window.View.ActualWidth + 0.5, barRight, "The Filter bar runs past the page.");
            var tops = new HashSet<double>();
            foreach (var chip in Chips())
            {
                var at = chip.TransformToVisual(bar).TransformPoint(new Point(0, 0));
                var name = AutomationProperties.GetName(chip);
                Assert.IsGreaterThanOrEqualTo(-0.5, at.X, $"'{name}' starts left of the Filter bar.");
                Assert.IsLessThanOrEqualTo(bar.ActualWidth + 0.5, at.X + chip.ActualWidth,
                    $"'{name}' ends at {at.X + chip.ActualWidth}, past the Filter bar's right edge at {bar.ActualWidth}.");
                Assert.IsLessThanOrEqualTo(bar.ActualHeight + 0.5, at.Y + chip.ActualHeight,
                    $"'{name}' ends below the Filter bar.");
                tops.Add(Math.Round(at.Y));
            }

            Assert.IsGreaterThan(1, tops.Count, "The Tag chips sit on one line.");
        }
        finally
        {
            window.Close();
        }
    }

    // The shell loads the page once the database is ready. A load the window started by itself would race it, and
    // after the database is gone its failure would end the app.
    [UITestMethod]
    public async Task Opening_the_window_does_not_load_the_page_by_itself()
    {
        using var db = await DatabaseAsync();
        var tags = new CountingTagService(db.Get<ITagService>());
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), tags, db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", Ct);
            Toggle(VisualTree.Find<ToggleButton>(window.View, "FilterButton"));
            await UiWait.UntilAsync(() => FindByName<ComboBox>(window.View, "Priority filter").IsLoaded,
                () => "The Filter bar never loaded.", Ct);

            Assert.AreEqual(0, tags.ListTagsCalls, "The window loaded the page before the shell asked.");
            var priority = FindByName<ComboBox>(window.View, "Priority filter");
            Assert.AreEqual(0, priority.SelectedIndex, "Any priority is selected.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Each_Filter_narrows_the_list_and_Clear_filters_keeps_the_search()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Pay rent", priority: Priority.High);
        await AddAsync(db, "Pay bills", due: today.AddDays(3));
        var tagged = await AddAsync(db, "Water plants");
        await db.Get<ITagService>().AddTagToTaskAsync(tagged, "home", Ct);
        await AddAsync(db, "Call mum");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            await WaitForRowsAsync(view, "Pay rent", "Pay bills", "Water plants", "Call mum");
            Toggle(VisualTree.Find<ToggleButton>(view, "FilterButton"));

            var priority = FindByName<ComboBox>(view, "Priority filter");
            CollectionAssert.AreEqual(
                new[] { "Any priority", "Low or higher", "Medium or higher", "High" }, priority.Items.ToList());
            priority.SelectedIndex = 3;
            await WaitForRowsAsync(view, "Pay rent");
            await ClearFiltersAsync(view);
            await WaitForRowsAsync(view, "Pay rent", "Pay bills", "Water plants", "Call mum");
            Assert.AreEqual(0, priority.SelectedIndex, "Clear filters left the priority Filter set.");

            Toggle(FindByName<ToggleButton>(view, "Has due filter"));
            await WaitForRowsAsync(view, "Pay bills");
            await ClearFiltersAsync(view);

            Toggle(FindByName<ToggleButton>(view, "Has reminder filter"));
            await WaitForRowsAsync(view);
            await ClearFiltersAsync(view);

            Toggle(await WaitForAsync(() => FindByNameOrNull<ToggleButton>(view, "#home"), "No #home Tag chip."));
            await WaitForRowsAsync(view, "Water plants");
            await ClearFiltersAsync(view);
            await WaitForRowsAsync(view, "Pay rent", "Pay bills", "Water plants", "Call mum");

            // Clear filters turns the Filters off and keeps the search.
            viewModel.SearchText = "pay";
            await viewModel.ApplySearchCommand.ExecuteAsync(null);
            Toggle(FindByName<ToggleButton>(view, "Has due filter"));
            await WaitForRowsAsync(view, "Pay bills");
            await ClearFiltersAsync(view);
            await WaitForRowsAsync(view, "Pay rent", "Pay bills");
            Assert.AreEqual("pay", VisualTree.Find<TextBox>(view, "SearchBox").Text);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Ctrl_F_focuses_search_typing_narrows_the_list_and_Esc_clears_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Alpha report");
        await AddAsync(db, "Beta review");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            await WaitForRowsAsync(view, "Alpha report", "Beta review");
            var search = VisualTree.Find<TextBox>(view, "SearchBox");
            Assert.AreEqual("SearchBox", AutomationProperties.GetAutomationId(search));
            Assert.AreEqual("Search (Ctrl+F)", AutomationProperties.GetName(search));
            Assert.AreEqual("Search in Inbox", search.PlaceholderText);

            Assert.IsTrue(view.TryShortcut(VirtualKey.F, VirtualKeyModifiers.Control));
            await WaitForFocusAsync(search);
            search.Text = "alpha";
            await WaitForRowsAsync(view, "Alpha report");

            Assert.IsTrue(view.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => search.Text.Length == 0, () => "Esc did not clear the search.", Ct);
            await WaitForRowsAsync(view, "Alpha report", "Beta review");
            Assert.IsFalse(view.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None),
                "Esc with no search text should be left to others.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Enter_in_quick_add_adds_the_Task_to_the_current_List_and_clears_the_box()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var quickAdd = VisualTree.Find<TextBox>(view, "QuickAddBox");
            Assert.AreEqual("Add a task to Inbox", quickAdd.PlaceholderText);
            await WaitForFocusAsync(quickAdd);

            quickAdd.Text = "Pay rent";
            Assert.IsTrue(view.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));

            await WaitForRowsAsync(view, "Pay rent");
            await UiWait.UntilAsync(() => quickAdd.Text.Length == 0, () => "Quick add kept its text.", Ct);
            var inbox = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
            Assert.AreEqual(inbox, (await db.QueryAsync(c => c.Tasks)).Single().ListId);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Ctrl_N_moves_focus_from_search_to_quick_add_and_on_Completed_shows_the_Default_List_first()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var quickAdd = VisualTree.Find<TextBox>(view, "QuickAddBox");
            Assert.IsTrue(view.TryShortcut(VirtualKey.F, VirtualKeyModifiers.Control));
            await WaitForFocusAsync(VisualTree.Find<TextBox>(view, "SearchBox"));
            Assert.IsTrue(view.TryShortcut(VirtualKey.N, VirtualKeyModifiers.Control));
            await WaitForFocusAsync(quickAdd);

            await viewModel.ShowSmartViewAsync(SmartView.Completed, Ct);
            Assert.AreEqual(Visibility.Collapsed, quickAdd.Visibility, "The Completed Smart View offers quick add.");
            Assert.IsTrue(view.TryShortcut(VirtualKey.N, VirtualKeyModifiers.Control));

            var heading = VisualTree.Find<TextBlock>(view, "PageHeading");
            await UiWait.UntilAsync(() => heading.Text == "Inbox", () => $"The heading read '{heading.Text}'.", Ct);
            Assert.AreEqual(Visibility.Visible, quickAdd.Visibility);
            await WaitForFocusAsync(quickAdd);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(null, "No Tasks in Inbox yet. Type above to add one.", DisplayName = "Inbox")]
    [DataRow(SmartView.Today, "Nothing due today.", DisplayName = "Today")]
    [DataRow(SmartView.Overdue, "Nothing is overdue.", DisplayName = "Overdue")]
    [DataRow(SmartView.Completed, "No completed Tasks.", DisplayName = "Completed")]
    public async Task An_empty_page_says_so_in_plain_text(SmartView? page, string message)
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.ShowSmartViewAsync(page, Ct);
            var state = VisualTree.Find<FrameworkElement>(window.View, "EmptyState");
            var text = VisualTree.Find<TextBlock>(window.View, "EmptyMessage");
            await UiWait.UntilAsync(() => state.Visibility == Visibility.Visible && text.Text == message,
                () => $"The empty state read '{text.Text}' ({state.Visibility}).", Ct);
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<Button>(window.View, "ClearSearchButton").Visibility,
                "Clear search shows with no search text.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_search_without_results_offers_Clear_search_which_brings_the_Tasks_back()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var state = VisualTree.Find<FrameworkElement>(view, "EmptyState");
            await WaitForRowsAsync(view, "Pay rent");
            Assert.AreEqual(Visibility.Collapsed, state.Visibility, "The empty state shows beside Tasks.");

            VisualTree.Find<TextBox>(view, "SearchBox").Text = "zebra";
            var text = VisualTree.Find<TextBlock>(view, "EmptyMessage");
            await UiWait.UntilAsync(() => text.Text == "No Tasks match 'zebra'",
                () => $"The empty state read '{text.Text}'.", Ct);
            var clear = VisualTree.Find<Button>(view, "ClearSearchButton");
            Assert.AreEqual(Visibility.Visible, clear.Visibility);
            Assert.AreEqual("Clear search", clear.Content);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(clear)).Invoke();
            await WaitForRowsAsync(view, "Pay rent");
            Assert.AreEqual("", VisualTree.Find<TextBox>(view, "SearchBox").Text);
            Assert.AreEqual(Visibility.Collapsed, state.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Typing_a_search_just_before_the_window_closes_does_not_reload_after_it_closed()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        var reloads = 0;
        ((System.ComponentModel.INotifyPropertyChanged)viewModel.ApplySearchCommand).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.ApplySearchCommand.ExecutionTask))
            {
                reloads++;
            }
        };
        try
        {
            await viewModel.LoadAsync(Ct);
            // Starts the pause-in-typing timer; the window closes before it runs out.
            VisualTree.Find<TextBox>(window.View, "SearchBox").Text = "pay";
        }
        finally
        {
            window.Close();
        }

        // The search would reload over the database the closed window no longer owns.
        await Task.Delay(TimeSpan.FromMilliseconds(750), Ct);
        Assert.AreEqual(0, reloads, "The search reloaded the page after its window closed.");
    }

    [UITestMethod]
    public async Task Closing_the_window_drops_the_search_that_Clear_search_left_waiting()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            viewModel.SearchText = "zebra";
            await viewModel.ApplySearchCommand.ExecuteAsync(null);
            // Clear search empties the search box, and its TextChanged waits for typing to pause like a keystroke.
            await viewModel.ClearSearchCommand.ExecuteAsync(null);
            await UiWait.UntilAsync(() => window.View.IsSearchPending,
                () => "Clearing the search left no search waiting.", Ct);
        }
        finally
        {
            window.Close();
        }

        // Unloaded comes only after Close returns, once the caller may have let the database go.
        Assert.IsFalse(window.View.IsSearchPending, "A search still waits to reload after the window closed.");
    }

    [UITestMethod]
    public async Task Closing_the_window_cancels_the_search_still_loading()
    {
        using var db = await DatabaseAsync();
        var search = new HeldSearch();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: search, listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", Ct);
            await viewModel.LoadAsync(Ct);
            VisualTree.Find<TextBox>(window.View, "SearchBox").Text = "pay";
            // Typing paused: the search started and now waits on the database.
            await UiWait.UntilAsync(() => search.Started, () => "The search never started.", Ct);
        }
        finally
        {
            window.Close();
        }

        Assert.IsTrue(search.Cancelled, "The search still loading went on after its window closed.");
        var run = viewModel.ApplySearchCommand.ExecutionTask!;
        await UiWait.UntilAsync(() => run.IsCompleted, () => "The cancelled search never ended.", Ct);
        Assert.IsTrue(run.IsCompletedSuccessfully, $"The cancelled search ended {run.Status}, which the app reports as a crash.");
    }

    [UITestMethod]
    public async Task Stopping_search_for_the_exit_drops_the_waiting_search_and_ignores_later_typing()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        var reloads = 0;
        ((System.ComponentModel.INotifyPropertyChanged)viewModel.ApplySearchCommand).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.ApplySearchCommand.ExecutionTask))
            {
                reloads++;
            }
        };
        try
        {
            await viewModel.LoadAsync(Ct);
            var searchBox = VisualTree.Find<TextBox>(window.View, "SearchBox");
            searchBox.Text = "pay";
            await UiWait.UntilAsync(() => window.View.IsSearchPending, () => "Typing left no search waiting.", Ct);

            // The exit starts while the window stays open: the flush and the host stop come next.
            await window.PrepareForExitAsync();
            Assert.IsFalse(window.View.IsSearchPending, "A search still waits to reload as the exit starts.");
            // Typing during the exit would otherwise reload over a host being stopped.
            searchBox.Text = "pay rent";
            Assert.IsFalse(window.View.IsSearchPending, "Typing during the exit left a search waiting.");
            await Task.Delay(TimeSpan.FromMilliseconds(750), Ct);
            Assert.AreEqual(0, reloads, "The search reloaded the page during the exit.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Stopping_search_for_the_exit_cancels_the_search_still_loading()
    {
        using var db = await DatabaseAsync();
        var search = new HeldSearch();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: search, listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", Ct);
            await viewModel.LoadAsync(Ct);
            VisualTree.Find<TextBox>(window.View, "SearchBox").Text = "pay";
            await UiWait.UntilAsync(() => search.Started, () => "The search never started.", Ct);

            await window.PrepareForExitAsync();

            Assert.IsTrue(search.Cancelled, "The search still loading went on as the exit started.");
            var run = viewModel.ApplySearchCommand.ExecutionTask!;
            await UiWait.UntilAsync(() => run.IsCompleted, () => "The cancelled search never ended.", Ct);
            Assert.IsTrue(run.IsCompletedSuccessfully, $"The cancelled search ended {run.Status}, which the app reports as a crash.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Preparing_for_the_exit_waits_for_the_cancelled_search_to_end()
    {
        using var db = await DatabaseAsync();
        var search = new SlowToStopSearch();
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: search, listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", Ct);
            await viewModel.LoadAsync(Ct);
            VisualTree.Find<TextBox>(window.View, "SearchBox").Text = "pay";
            await UiWait.UntilAsync(() => search.Started, () => "The search never started.", Ct);

            var preparing = window.PrepareForExitAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);
            // The flush and the host stop come next: the search must not overlap them.
            Assert.IsFalse(preparing.IsCompleted, "The exit went on while the cancelled search still ran.");

            search.Release.SetResult();
            await UiWait.UntilAsync(() => preparing.IsCompleted, () => "The exit never went on after the search ended.", Ct);
            Assert.IsTrue(preparing.IsCompletedSuccessfully, $"Preparing for the exit ended {preparing.Status}.");
        }
        finally
        {
            search.Release.TrySetResult();
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Preparing_for_the_exit_turns_off_input_so_Esc_and_the_filter_chips_start_no_reload()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            viewModel.ToggleFilterBarCommand.Execute(null);
            var hasDue = FindByName<ToggleButton>(window.View, "Has due filter");
            await UiWait.UntilAsync(() => hasDue.ActualWidth > 0, () => "The Has due chip never showed.", Ct);
            viewModel.SearchText = "pay";

            await window.PrepareForExitAsync();

            // Esc would clear the search and reload the page over a host being stopped.
            Assert.IsFalse(window.View.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None), "Esc ran during the exit.");
            Assert.AreEqual("pay", viewModel.SearchText, "Esc cleared the search during the exit.");
            Assert.IsNull(viewModel.ClearSearchCommand.ExecutionTask, "Esc started a reload during the exit.");

            // A screen reader's Toggle on a chip that takes no input is refused with UIA_E_ELEMENTNOTENABLED.
            var refused = Assert.ThrowsExactly<System.Runtime.InteropServices.COMException>(
                () => Toggle(hasDue), "The Has due chip took input during the exit.");
            Assert.AreEqual(unchecked((int)0x80040200), refused.HResult, "The chip's Toggle failed for another reason.");
            Assert.IsFalse(viewModel.HasDueFilter, "The Has due chip turned on during the exit.");
            Assert.IsNull(viewModel.ToggleHasDueFilterCommand.ExecutionTask, "The Has due chip started a reload during the exit.");
        }
        finally
        {
            window.Close();
        }
    }

    // A search that ignores its cancellation until the test releases it, as a query already sent to the database does.
    private sealed class SlowToStopSearch : ITaskSearchService
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Started { get; private set; }

        public async Task<IReadOnlyList<TaskItem>> SearchAsync(TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Started = true;
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
    }

    // A search that waits on the database until it is cancelled.
    private sealed class HeldSearch : ITaskSearchService
    {
        private CancellationToken _token;

        public bool Started { get; private set; }

        public bool Cancelled => _token.IsCancellationRequested;

        public async Task<IReadOnlyList<TaskItem>> SearchAsync(TaskSearchQuery query, CancellationToken cancellationToken = default)
        {
            Started = true;
            _token = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }

    [UITestMethod]
    public async Task Load_more_completed_shows_on_the_Completed_view_while_more_pages_remain()
    {
        using var db = await DatabaseAsync();
        for (var i = 0; i <= IDueService.CompletedPageSize; i++)
        {
            await AddAsync(db, $"Done {i}", completed: true);
        }

        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Completed, Ct);
            var more = VisualTree.Find<Button>(window.View, "LoadMoreCompletedButton");
            Assert.AreEqual("Load more completed tasks", more.Content);
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Visible,
                () => "Load more is hidden with more completed Tasks to load.", Ct);
            Assert.AreEqual(IDueService.CompletedPageSize, viewModel.Tasks.Count);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(more)).Invoke();
            await UiWait.UntilAsync(() => viewModel.Tasks.Count == IDueService.CompletedPageSize + 1,
                () => $"{viewModel.Tasks.Count} completed Tasks after Load more.", Ct);
            await UiWait.UntilAsync(() => more.Visibility == Visibility.Collapsed,
                () => "Load more still shows with every completed Task loaded.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_notification_fallback_shows_as_a_warning_InfoBar_until_dismissed()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var bar = VisualTree.Find<InfoBar>(window.View, "NotificationBanner");
            Assert.IsFalse(bar.IsOpen, "The notification banner shows before notifications fell back.");

            viewModel.ShowNotificationBanner("Reminders show as tray balloons.");

            await UiWait.UntilAsync(() => bar.IsOpen, () => "The notification banner did not open.", Ct);
            Assert.AreEqual("Notifications unavailable", AutomationProperties.GetName(bar));
            Assert.AreEqual("Reminders show as tray balloons.", bar.Message);
            Assert.AreEqual(InfoBarSeverity.Warning, bar.Severity);
            Assert.IsTrue(bar.IsClosable);
            await UiWait.UntilAsync(() => FindCloseButton(bar) is not null,
                () => "The notification banner has no close button.", Ct);

            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(FindCloseButton(bar)!)).Invoke();

            await UiWait.UntilAsync(() => viewModel.NotificationBanner is null && !bar.IsOpen,
                () => "Closing the notification banner did not dismiss it.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button? FindCloseButton(InfoBar bar) =>
        VisualTree.Descendants(bar).OfType<Button>().FirstOrDefault(b => b.Name == "CloseButton");

    private static void Toggle(ToggleButton button) =>
        ((ToggleButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(button)).Toggle();

    private static void Invoke(MenuFlyoutItem item) =>
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(item)).Invoke();

    private async Task ClearFiltersAsync(MainView view)
    {
        var clear = FindByName<Button>(view, "Clear filters");
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(clear)).Invoke();
        await UiWait.UntilAsync(() => !view.ViewModel.HasFilter, () => "Clear filters left a Filter on.", Ct);
    }

    // By the name UI Automation reads, as the UI tests find them: the accessible name, else the content.
    private static T? FindByNameOrNull<T>(DependencyObject root, string name) where T : FrameworkElement =>
        VisualTree.Descendants(root).OfType<T>()
            .FirstOrDefault(e => FrameworkElementAutomationPeer.CreatePeerForElement(e)?.GetName() == name);

    private static T FindByName<T>(DependencyObject root, string name) where T : FrameworkElement =>
        FindByNameOrNull<T>(root, name) ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}'.");

    private async Task<T> WaitForAsync<T>(Func<T?> find, string failure) where T : class
    {
        await UiWait.UntilAsync(() => find() is not null, () => failure, Ct);
        return find()!;
    }

    private Task WaitForFocusAsync(Control control) => UiWait.UntilAsync(
        () => control.XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), control),
        () => $"{control.Name} does not have keyboard focus.", Ct);

    // The rows the list shows; containers it keeps for recycling stay in the visual tree but hold no item.
    private static List<string> RowNames(ListView list) =>
        [.. VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer)
            .Select(AutomationProperties.GetName)];

    private Task WaitForRowsAsync(MainView view, params string[] titles)
    {
        var list = VisualTree.Find<ListView>(view, "OpenTaskList");
        return UiWait.UntilAsync(() => RowNames(list).SequenceEqual(titles),
            () => $"OpenTaskList: expected [{string.Join(", ", titles)}], was [{string.Join(", ", RowNames(list))}]"
                + $" (Tasks [{string.Join(", ", view.ViewModel.Tasks.Select(t => t.Title))}], search '{view.ViewModel.SearchText}', error '{view.ViewModel.ErrorMessage}').", Ct);
    }

    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(TempDatabase db)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        await UiWait.UntilAsync(() => window.View.IsLoaded, () => "The main view never loaded.", Ct);
        return (window, viewModel);
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private static async Task<Guid> AddAsync(
        TempDatabase db, string title, bool completed = false, DateOnly? due = null, Priority priority = Priority.None)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var order = await context.Tasks.CountAsync();
        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId,
            Title = title,
            Priority = priority,
            IsCompleted = completed,
            CompletedUtc = completed ? now : null,
            DueDate = due,
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task.Id;
    }

    // Answers every rename prompt with one name.
    private sealed class RenamingDialogs(string name) : IListDialogs
    {
        public Task<bool> ConfirmDeleteAsync(string listName, int taskCount) => Task.FromResult(false);

        public Task<string?> PromptNameAsync(string currentName) => Task.FromResult<string?>(name);
    }

    // Counts the page loads: every load lists the Tags first.
    private sealed class CountingTagService(ITagService inner) : ITagService
    {
        public int ListTagsCalls { get; private set; }

        public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken cancellationToken = default)
        {
            ListTagsCalls++;
            return inner.ListTagsAsync(cancellationToken);
        }

        public Task<Tag> CreateTagAsync(string name, CancellationToken cancellationToken = default) =>
            inner.CreateTagAsync(name, cancellationToken);

        public Task<Tag> AddTagToTaskAsync(Guid taskId, string name, CancellationToken cancellationToken = default) =>
            inner.AddTagToTaskAsync(taskId, name, cancellationToken);

        public Task RemoveTagFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) =>
            inner.RemoveTagFromTaskAsync(taskId, tagId, cancellationToken);

        public Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.DeleteTagAsync(tagId, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListTasksByTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            inner.ListTasksByTagAsync(tagId, cancellationToken);
    }
}
