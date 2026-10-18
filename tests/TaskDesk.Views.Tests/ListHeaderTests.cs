using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
/// The header of the list area over a real database: the page heading and List menu, the Filter button and
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
    public async Task The_List_menu_shows_on_a_List_page_and_renames_and_colours_the_List()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = await OpenAsync(db);
        var dialogs = new RenamingDialogs("Home");
        var lists = new ListsViewModel(db.Get<IListService>(), dialogs);
        try
        {
            await lists.LoadAsync(Ct);
            window.View.Lists = lists;
            await viewModel.LoadAsync(Ct);
            var button = VisualTree.Find<Button>(window.View, "ListMenuButton");
            Assert.AreEqual("List settings", AutomationProperties.GetName(button));
            Assert.AreEqual(Visibility.Visible, button.Visibility);

            var menu = (MenuFlyout)button.Flyout;
            Assert.AreEqual("Rename", ((MenuFlyoutItem)menu.Items[0]).Text);
            var colour = (MenuFlyoutSubItem)menu.Items[1];
            Assert.AreEqual("Colour", colour.Text);
            CollectionAssert.AreEqual(
                new[] { "None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple" },
                colour.Items.Cast<MenuFlyoutItem>().Select(i => i.Text).ToList());

            Invoke((MenuFlyoutItem)menu.Items[0]);
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.IsDefault).Name == "Home",
                () => "Rename did not rename the shown List.", Ct);

            Invoke(colour.Items.Cast<MenuFlyoutItem>().Single(i => i.Text == "Green"));
            await UiWait.UntilAsync(() => lists.Lists.Single(l => l.IsDefault).ColorHex == "#107C10",
                () => "Colour did not colour the shown List.", Ct);

            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            Assert.AreEqual(Visibility.Collapsed, button.Visibility, "A Smart View has no List menu.");
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
            var filter = VisualTree.Find<ToggleButton>(window.View, "FilterButton");
            var bar = VisualTree.Find<FrameworkElement>(window.View, "FilterBar");
            Assert.AreEqual("Filter", AutomationProperties.GetName(filter));
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
