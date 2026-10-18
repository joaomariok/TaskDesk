using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using TaskDesk.Views.Dialogs;
using Windows.UI;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The Lists and Tags under the Smart Views: one scroll area with the Lists (colour dot or hollow ring, open count),
/// "+ New List" and the Tags; the rail's List initials in their colour ring; the context menu's Rename, Colour and
/// Delete through the dialogs; a Tag filtering the page; and the order a drag leaves saved.
/// </summary>
[TestClass]
public sealed class SidebarListsAndTagsTests
{
    public TestContext TestContext { get; set; } = null!;

    [UITestMethod]
    public async Task Lists_show_a_colour_dot_or_a_hollow_ring_and_their_open_count_only_above_zero()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Groceries", 0, null));
        lists.Lists.Add(NewList("Work", 5, "#D13438"));
        try
        {
            window.Activate();
            var rows = await RowsAsync(window, "ListsList", 2);

            CollectionAssert.AreEqual(new[] { "Groceries", "Work" }, rows.Select(AutomationProperties.GetName).ToList());
            var groceries = Badge(rows[0]);
            var work = Badge(rows[1]);
            Assert.AreEqual(Visibility.Collapsed, groceries.Visibility, "A count of 0 is not shown.");
            Assert.AreEqual(Visibility.Visible, work.Visibility);
            Assert.AreEqual(5, work.Value);

            Assert.AreEqual(Color.FromArgb(255, 0xD1, 0x34, 0x38), Colour(VisibleEllipse(rows[1]).Fill), "Work's dot is its colour.");
            var ring = VisibleEllipse(rows[0]);
            Assert.IsGreaterThan(0, ring.StrokeThickness, "A List without a colour shows a hollow ring.");
            Assert.AreEqual(Colors.Transparent, Colour(ring.Fill ?? new SolidColorBrush(Colors.Transparent)));

            lists.Lists[0].OpenTaskCount = 4;
            Assert.AreEqual(Visibility.Visible, groceries.Visibility, "A count that rises above zero shows again.");
            Assert.AreEqual(4, groceries.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_List_whose_colour_is_removed_shows_the_hollow_ring_in_the_sidebar_and_the_rail()
    {
        var ct = TestContext.CancellationToken;
        var colours = await ThemeColours.ResolveAsync(ElementTheme.Light, ["ListStripeBrush"], ct);
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Work", 0, "#D13438"));
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = ElementTheme.Light;
            window.Activate();
            var row = (await RowsAsync(window, "ListsList", 1))[0];
            Assert.AreEqual(Color.FromArgb(255, 0xD1, 0x34, 0x38), Colour(VisibleEllipse(row).Fill));

            lists.Lists[0].ColorHex = null;

            var ring = VisibleEllipse(row);
            Assert.IsNotNull(ring.Stroke, "The List's old colour dot is still shown instead of the hollow ring.");
            Assert.AreEqual(colours["ListStripeBrush"], Colour(ring.Stroke));
            Assert.IsTrue(VisualTree.Descendants(row).OfType<Ellipse>().All(e => e.Fill is null || Colour(e.Fill) == Colors.Transparent),
                "An Ellipse still holds the List's old colour.");

            lists.Lists[0].ColorHex = "#D13438";
            window.Navigation.SetRail(true);
            var railRow = (await RowsAsync(window, "RailListsList", 1))[0];
            Assert.AreEqual(Color.FromArgb(255, 0xD1, 0x34, 0x38), Colour(VisibleEllipse(railRow).Stroke));

            lists.Lists[0].ColorHex = null;

            Assert.AreEqual(colours["ListStripeBrush"], Colour(VisibleEllipse(railRow).Stroke),
                "The rail's ring keeps the List's old colour.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Lists_error_that_is_cleared_takes_no_room_again()
    {
        using var db = new TempDatabase();
        var (window, _, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Work", 0, null));
        try
        {
            window.Activate();
            await RowsAsync(window, "ListsList", 1);
            var area = VisualTree.Find<StackPanel>(window.Content, "NewListArea");
            var error = VisualTree.Descendants(area).OfType<TextBlock>().First();

            lists.ErrorMessage = "That name is taken.";
            area.UpdateLayout();
            Assert.IsGreaterThan(0, error.DesiredSize.Height, "The error is not shown.");

            lists.ErrorMessage = null;
            area.UpdateLayout();

            Assert.AreEqual(Visibility.Collapsed, error.Visibility, "The cleared error is still shown.");
            Assert.AreEqual(0, error.DesiredSize.Height, "The cleared error still takes room.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Lists_new_List_and_Tags_share_one_scroll_area_below_the_Smart_Views_in_that_order()
    {
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Inbox", 1, null));
        lists.Lists.Add(NewList("Work", 2, null));
        viewModel.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });
        try
        {
            window.Activate();
            await RowsAsync(window, "ListsList", 2);
            await RowsAsync(window, "TagList", 1);
            var root = window.Content;
            var scroll = VisualTree.Find<ScrollViewer>(root, "SidebarScroll");
            var smartViews = WithId(root, "SmartViewList")!;
            var listsList = WithId(root, "ListsList")!;
            var newList = WithId(root, "NewListButton")!;
            var tags = WithId(root, "TagList")!;

            Assert.IsFalse(IsInside(smartViews, scroll), "The Smart Views must stay fixed above the scroll area.");
            Assert.IsTrue(IsInside(listsList, scroll), "The Lists are not in the scroll area.");
            Assert.IsTrue(IsInside(newList, scroll), "'+ New List' is not in the scroll area.");
            Assert.IsTrue(IsInside(tags, scroll), "The Tags are not in the scroll area.");

            var smartViewsBottom = TopOf(smartViews, root) + smartViews.ActualHeight;
            var listsTop = TopOf(listsList, root);
            var newListTop = TopOf(newList, root);
            var tagsTop = TopOf(tags, root);
            Assert.IsTrue(smartViewsBottom <= listsTop && listsTop < newListTop && newListTop < tagsTop,
                $"Expected Smart Views (bottom {smartViewsBottom}) <= Lists ({listsTop}) < '+ New List' ({newListTop}) < Tags ({tagsTop}).");
            Assert.IsLessThan(64, listsTop - smartViewsBottom, "The Lists start right under the Smart Views (only the Lists header between).");
            Assert.IsInRange(0, 16, newListTop - (listsTop + listsList.ActualHeight), "'+ New List' sits right under the last List.");

            // The scroll area is the only scroller: the Lists and Tags never scroll on their own.
            Assert.AreEqual(ScrollMode.Disabled, ScrollViewer.GetVerticalScrollMode(listsList));
            Assert.AreEqual(ScrollMode.Disabled, ScrollViewer.GetVerticalScrollMode(tags));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    [DataRow(ElementTheme.Light)]
    [DataRow(ElementTheme.Dark)]
    public async Task Tag_names_line_up_with_List_names_behind_a_secondary_hash(ElementTheme theme)
    {
        var ct = TestContext.CancellationToken;
        var colours = await ThemeColours.ResolveAsync(theme, ["TextFillColorSecondaryBrush"], ct);
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Work", 2, null));
        viewModel.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = theme;
            window.Activate();
            var listRow = (await RowsAsync(window, "ListsList", 1))[0];
            var tagRow = (await RowsAsync(window, "TagList", 1))[0];
            var root = window.Content;
            var listName = TextIn(listRow, "Work");
            var tagName = TextIn(tagRow, "home");
            var hash = TextIn(tagRow, "#");

            Assert.AreEqual(LeftOf(listName, root), LeftOf(tagName, root), 0.5, "Tag names line up with List names.");
            Assert.IsLessThan(LeftOf(tagName, root), LeftOf(hash, root), "The '#' sits in the icon column, left of the name.");
            Assert.AreEqual(colours["TextFillColorSecondaryBrush"], Colour(hash.Foreground), $"{theme}: the '#' is secondary text.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Tags_section_is_hidden_while_there_are_no_Tags()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Inbox", 0, null));
        try
        {
            window.Activate();
            await RowsAsync(window, "ListsList", 1);
            var tags = WithId(window.Content, "TagList")!;
            Assert.IsFalse(IsShown(tags), "The Tags section shows without Tags.");

            viewModel.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });

            await UiWait.UntilAsync(() => IsShown(tags), () => "The Tags section did not show for a new Tag.", ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task In_the_rail_each_List_shows_its_initial_in_a_ring_of_its_colour_and_Tags_move_behind_a_hash_button()
    {
        var ct = TestContext.CancellationToken;
        var colours = await ThemeColours.ResolveAsync(ElementTheme.Light, ["ListStripeBrush"], ct);
        using var db = new TempDatabase();
        var (window, viewModel, lists) = NewWindow(db);
        lists.Lists.Add(NewList("Work", 0, null));
        lists.Lists.Add(NewList("groceries", 0, null));
        lists.Lists.Add(NewList("Home", 0, "#D13438"));
        viewModel.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "home" });
        try
        {
            ((FrameworkElement)window.Content).RequestedTheme = ElementTheme.Light;
            window.Activate();
            await RowsAsync(window, "ListsList", 3);

            window.Navigation.SetRail(true);

            var rows = await RowsAsync(window, "RailListsList", 3);
            CollectionAssert.AreEqual(new[] { "W", "G", "H" },
                rows.Select(r => VisualTree.Descendants(r).OfType<TextBlock>().Single(t => t.Visibility == Visibility.Visible).Text).ToList());
            CollectionAssert.AreEqual(new[] { "Work", "groceries", "Home" }, rows.Select(AutomationProperties.GetName).ToList(),
                "Rail Lists keep their names.");
            Assert.AreEqual(Color.FromArgb(255, 0xD1, 0x34, 0x38), Colour(VisibleEllipse(rows[2]).Stroke));
            Assert.AreEqual(colours["ListStripeBrush"], Colour(VisibleEllipse(rows[0]).Stroke));
            Assert.IsFalse(IsShown(WithId(window.Content, "NewListButton")!), "The rail has no '+ New List'.");
            Assert.IsFalse(IsShown(WithId(window.Content, "TagList")!), "The rail shows the Tags behind its # button.");
            Assert.IsTrue(IsShown(WithId(window.Content, "RailTagsButton")!), "The rail has a # button for the Tags.");

            window.Navigation.SetRail(false);

            await RowsAsync(window, "ListsList", 3);
            Assert.IsTrue(IsShown(WithId(window.Content, "TagList")!));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task New_List_opens_a_name_box_whose_List_appears_as_a_row()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var (window, viewModel, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await lists.LoadAsync(ct);
            await RowsAsync(window, "ListsList", 1);
            var button = (Button)WithId(window.Content, "NewListButton")!;
            var box = (TextBox)WithId(window.Content, "NewListBox")!;
            Assert.AreEqual("New list", AutomationProperties.GetName(button));
            Assert.AreEqual("New list name", AutomationProperties.GetName(box));
            Assert.IsFalse(IsShown(box));

            new ButtonAutomationPeer(button).Invoke();

            await UiWait.UntilAsync(() => IsShown(box) && !IsShown(button), () => "'+ New List' did not open the name box.", ct);
            box.Text = "Groceries";
            Assert.AreEqual("Groceries", lists.NewListName, "The box edits the new List's name.");
            await window.Navigation.CreateListAsync();

            var rows = await RowsAsync(window, "ListsList", 2);
            Assert.AreEqual("Groceries", AutomationProperties.GetName(rows[1]));
            Assert.IsTrue(IsShown(button) && !IsShown(box), "The box closes once the List is created.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_context_menu_renames_colours_and_deletes_a_List_through_the_dialogs()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        await db.Get<IListService>().CreateListAsync("Groceries", ct);
        var (window, viewModel, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await lists.LoadAsync(ct);
            var rows = await RowsAsync(window, "ListsList", 2);
            var menu = await OpenMenuAsync(rows[1]);
            CollectionAssert.AreEqual(new[] { "Rename", "Colour", "Delete" }, menu.Items.Select(Text).ToList());
            var colour = (MenuFlyoutSubItem)menu.Items[1];
            CollectionAssert.AreEqual(new[] { "None", "Red", "Orange", "Yellow", "Green", "Blue", "Purple" },
                colour.Items.Select(Text).ToList());
            Assert.IsTrue(menu.Items[2].IsEnabled, "Another List can be deleted.");

            Invoke(menu.Items[0]);
            var rename = await DialogHost.OpenDialogAsync(window.Content.XamlRoot, ct);
            VisualTree.Descendants(rename).OfType<TextBox>().Single().Text = "Food";
            DialogHost.Press(rename, "PrimaryButton");
            await UiWait.UntilAsync(() => Names(window, "ListsList").Contains("Food"),
                () => $"Lists were [{string.Join(", ", Names(window, "ListsList"))}].", ct);

            rows = await RowsAsync(window, "ListsList", 2);
            menu = await OpenMenuAsync(rows[1]);
            Invoke(((MenuFlyoutSubItem)menu.Items[1]).Items[1]); // Red
            await UiWait.UntilAsync(() => lists.Lists[1].ColorHex == "#D13438", () => "Red did not colour the List.", ct);
            var saved = (await db.QueryAsync(c => c.Lists)).Single(l => l.Name == "Food");
            Assert.AreEqual("#D13438", saved.ColorHex);

            rows = await RowsAsync(window, "ListsList", 2);
            menu = await OpenMenuAsync(rows[1]);
            Invoke(((MenuFlyoutSubItem)menu.Items[1]).Items[0]); // None
            await UiWait.UntilAsync(() => lists.Lists[1].ColorHex is null, () => "None did not remove the colour.", ct);
            Assert.IsNull((await db.QueryAsync(c => c.Lists)).Single(l => l.Name == "Food").ColorHex);

            await viewModel.ShowListAsync(saved.Id, ct);
            Assert.AreEqual("Food", viewModel.PageHeading);
            rows = await RowsAsync(window, "ListsList", 2);
            menu = await OpenMenuAsync(rows[1]);
            Invoke(menu.Items[2]);
            var delete = await DialogHost.OpenDialogAsync(window.Content.XamlRoot, ct);
            DialogHost.Press(delete, "PrimaryButton");
            await UiWait.UntilAsync(() => lists.Lists.Count == 1,
                () => $"Lists were [{string.Join(", ", lists.Lists.Select(l => l.Name))}]; error '{lists.ErrorMessage}'.", ct);
            await RowsAsync(window, "ListsList", 1);
            await UiWait.UntilAsync(() => viewModel.PageHeading == "Inbox" && viewModel.CurrentListId == lists.Lists[0].Id,
                () => $"Deleting the shown List left the page on '{viewModel.PageHeading}'.", ct);
            Assert.HasCount(1, await db.QueryAsync(c => c.Lists));

            rows = await RowsAsync(window, "ListsList", 1);
            menu = await OpenMenuAsync(rows[0]);
            Assert.IsFalse(menu.Items[2].IsEnabled, "The Default List cannot be deleted.");
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Clicking_a_List_opens_its_page_and_clicking_it_again_ends_a_search()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        var work = await db.Get<IListService>().CreateListAsync("Work", ct);
        await db.AddTaskAsync(inbox, "Pay rent", completed: false);
        await db.AddTaskAsync(work.Id, "Write report", completed: false);
        await db.AddTaskAsync(work.Id, "Call Ana", completed: false);
        var (window, viewModel, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            await lists.LoadAsync(ct);
            var rows = await RowsAsync(window, "ListsList", 2);
            var listsList = (ListView)WithId(window.Content, "ListsList")!;

            Click(rows[1]);
            await UiWait.UntilAsync(() => viewModel.PageHeading == "Work" && Titles(viewModel).SequenceEqual(["Write report", "Call Ana"]),
                () => $"The page shows {viewModel.PageHeading} with [{string.Join(", ", Titles(viewModel))}].", ct);
            Assert.AreEqual("Work", (listsList.SelectedItem as ListItemViewModel)?.Name, "Work's page selects Work.");

            viewModel.SearchText = "report";
            await UiWait.UntilAsync(() => Titles(viewModel).SequenceEqual(["Write report"]),
                () => $"The search shows [{string.Join(", ", Titles(viewModel))}].", ct);

            Click((await RowsAsync(window, "ListsList", 2))[1]); // already selected
            await UiWait.UntilAsync(() => viewModel.SearchText.Length == 0 && Titles(viewModel).Count == 2,
                () => $"Clicking Work again left '{viewModel.SearchText}' with [{string.Join(", ", Titles(viewModel))}].", ct);
            Assert.AreEqual("Work", (listsList.SelectedItem as ListItemViewModel)?.Name);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Tag_click_filters_by_it_and_its_context_menu_deletes_it_after_confirmation()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        var inbox = (await db.QueryAsync(c => c.Lists)).Single().Id;
        await db.AddTaskAsync(inbox, "Pay rent", completed: false);
        await db.AddTaskAsync(inbox, "Walk", completed: false);
        var payRent = (await db.QueryAsync(c => c.Tasks)).Single(t => t.Title == "Pay rent").Id;
        await db.Get<ITagService>().AddTagToTaskAsync(payRent, "home", ct);
        var (window, viewModel, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await viewModel.LoadAsync(ct);
            await lists.LoadAsync(ct);
            var tagRows = await RowsAsync(window, "TagList", 1);
            var tagList = (ListView)WithId(window.Content, "TagList")!;
            var listsList = (ListView)WithId(window.Content, "ListsList")!;
            await UiWait.UntilAsync(() => listsList.SelectedItem is ListItemViewModel { IsDefault: true },
                () => "The Default List page selects the Default List.", ct);

            tagList.SelectedIndex = 0;

            await UiWait.UntilAsync(() => viewModel.TagFilter?.Name == "home" && Titles(viewModel).SequenceEqual(["Pay rent"]),
                () => $"The page shows #{viewModel.TagFilter?.Name} with [{string.Join(", ", Titles(viewModel))}].", ct);
            Assert.IsNull(listsList.SelectedItem, "A Tag's page selects no List.");

            listsList.SelectedIndex = 0; // the Default List shows it again
            await UiWait.UntilAsync(() => viewModel.TagFilter is null && tagList.SelectedItem is null,
                () => "Selecting the Default List did not leave the Tag's page.", ct);

            await viewModel.ShowSmartViewAsync(SmartView.All, ct);
            Assert.IsNull(listsList.SelectedItem, "A Smart View selects no List.");

            var menu = await OpenMenuAsync(tagRows[0]);
            CollectionAssert.AreEqual(new[] { "Delete" }, menu.Items.Select(Text).ToList());
            Invoke(menu.Items[0]);
            var confirm = await DialogHost.OpenDialogAsync(window.Content.XamlRoot, ct);
            Assert.IsTrue(DialogHost.Texts(confirm).Any(t => t.Contains("#home", StringComparison.Ordinal)));
            DialogHost.Press(confirm, "PrimaryButton");

            await UiWait.UntilAsync(() => viewModel.Tags.Count == 0, () => "The Tag was not deleted.", ct);
            Assert.HasCount(0, await db.QueryAsync(c => c.Tags));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_List_dropped_into_a_gap_lands_there_and_keeps_that_order_after_a_reload()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        foreach (var name in new[] { "A", "B", "C" })
        {
            await db.Get<IListService>().CreateListAsync(name, ct);
        }

        var (window, viewModel, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await lists.LoadAsync(ct);
            await RowsAsync(window, "ListsList", 4);
            var c = lists.Lists[3];

            // C dropped into the gap between Inbox and A.
            await window.Navigation.DropListAsync(c, 1);

            await lists.LoadAsync(ct);
            CollectionAssert.AreEqual(new[] { "Inbox", "C", "A", "B" }, lists.Lists.Select(l => l.Name).ToList());
            await UiWait.UntilAsync(() => Names(window, "ListsList").SequenceEqual(["Inbox", "C", "A", "B"]),
                () => $"Rows were [{string.Join(", ", Names(window, "ListsList"))}].", ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_List_dropped_before_the_first_or_after_the_last_List_moves_there_and_its_own_gap_does_nothing()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        foreach (var name in new[] { "A", "B", "C" })
        {
            await db.Get<IListService>().CreateListAsync(name, ct);
        }

        var (window, _, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await lists.LoadAsync(ct);
            await RowsAsync(window, "ListsList", 4);
            ListItemViewModel Named(string name) => lists.Lists.Single(l => l.Name == name);
            List<string> Order() => [.. lists.Lists.Select(l => l.Name)];

            await window.Navigation.DropListAsync(Named("A"), 4);
            CollectionAssert.AreEqual(new[] { "Inbox", "B", "C", "A" }, Order());

            await window.Navigation.DropListAsync(Named("A"), 0);
            CollectionAssert.AreEqual(new[] { "A", "Inbox", "B", "C" }, Order());

            // B sits at index 2: the gaps right above and right below it are its own.
            await window.Navigation.DropListAsync(Named("B"), 2);
            await window.Navigation.DropListAsync(Named("B"), 3);
            CollectionAssert.AreEqual(new[] { "A", "Inbox", "B", "C" }, Order());
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_drop_position_maps_to_the_gap_above_a_row_over_its_top_half_below_it_over_its_bottom_half_and_to_the_end_past_the_rows()
    {
        var ct = TestContext.CancellationToken;
        using var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(ct);
        foreach (var name in new[] { "A", "B" })
        {
            await db.Get<IListService>().CreateListAsync(name, ct);
        }

        var (window, _, lists) = NewWindow(db);
        try
        {
            window.Activate();
            await lists.LoadAsync(ct);
            await RowsAsync(window, "ListsList", 3);

            foreach (var rail in new[] { false, true })
            {
                window.Navigation.SetRail(rail);
                var id = rail ? "RailListsList" : "ListsList";
                var rows = await RowsAsync(window, id, 3);
                var list = VisualTree.Find<ListView>(window.Navigation, "ListsList");

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var top = row.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                    Assert.AreEqual(i, window.Navigation.GapAt(new Windows.Foundation.Point(20, top + 2)), $"{id} row {i}, top half");
                    Assert.AreEqual(i + 1, window.Navigation.GapAt(new Windows.Foundation.Point(20, top + row.ActualHeight - 2)), $"{id} row {i}, bottom half");
                }

                Assert.AreEqual(3, window.Navigation.GapAt(new Windows.Foundation.Point(20, list.ActualHeight + 500)), $"{id} past the rows");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private (MainWindow Window, MainViewModel ViewModel, ListsViewModel Lists) NewWindow(TempDatabase db)
    {
        MainWindow? window = null;
        var viewModel = new MainViewModel(db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock, db.Get<IDueService>(),
            db.Get<ITaskSearchService>(), listService: db.Get<IListService>(), taskCreator: db.Get<ITaskCreator>());
        var lists = new ListsViewModel(db.Get<IListService>(), new WinUiDialogs(new DialogService(() => window)));
        window = new MainWindow(viewModel, lists);
        return (window, viewModel, lists);
    }

    private static ListItemViewModel NewList(string name, int openTasks, string? colour) =>
        new(new ListSummary(new TaskList { Id = Guid.NewGuid(), Name = name, ColorHex = colour }, openTasks));

    private async Task<List<ListViewItem>> RowsAsync(MainWindow window, string id, int count)
    {
        List<ListViewItem> Rows() => Containers(window, id);
        await UiWait.UntilAsync(() => Rows().Count == count && Rows().All(r => r.IsLoaded),
            () => $"Expected {count} rows in {id}, found {Rows().Count}.", TestContext.CancellationToken);
        return Rows();
    }

    private static List<string> Names(MainWindow window, string id) =>
        [.. Containers(window, id).Select(AutomationProperties.GetName)];

    // The rows of the list with that id, in order (a recycled container may linger in the tree, so not its descendants).
    private static List<ListViewItem> Containers(MainWindow window, string id) => WithId(window.Content, id) is ListView list
        ? [.. Enumerable.Range(0, list.Items.Count).Select(list.ContainerFromIndex).OfType<ListViewItem>()]
        : [];

    private async Task<MenuFlyout> OpenMenuAsync(ListViewItem row)
    {
        var menu = (MenuFlyout)row.ContextFlyout;
        menu.ShowAt(row);
        await UiWait.UntilAsync(() => menu.IsOpen, () => "The context menu did not open.", TestContext.CancellationToken);
        return menu;
    }

    private static void Invoke(MenuFlyoutItemBase item) => new MenuFlyoutItemAutomationPeer((MenuFlyoutItem)item).Invoke();

    private static string Text(MenuFlyoutItemBase item) => item switch
    {
        MenuFlyoutItem i => i.Text,
        MenuFlyoutSubItem s => s.Text,
        _ => "",
    };

    private static FrameworkElement? WithId(UIElement root, string id) =>
        VisualTree.Descendants(root).OfType<FrameworkElement>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);

    private static InfoBadge Badge(DependencyObject row) => VisualTree.Descendants(row).OfType<InfoBadge>().Single();

    private static Ellipse VisibleEllipse(DependencyObject row) =>
        VisualTree.Descendants(row).OfType<Ellipse>().Single(e => e.Visibility == Visibility.Visible);

    private static TextBlock TextIn(DependencyObject row, string text) =>
        VisualTree.Descendants(row).OfType<TextBlock>().Single(t => t.Text == text);

    // Shown: it and every ancestor are visible.
    private static bool IsShown(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static double TopOf(UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;

    private static double LeftOf(UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

    private static Color Colour(Brush brush) => ((SolidColorBrush)brush).Color;

    // Clicks a List row as the owner would, through its automation peer.
    private static void Click(ListViewItem row) =>
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(row).GetPattern(PatternInterface.Invoke)
            ?? throw new AssertFailedException("The List row cannot be invoked.")).Invoke();

    private static List<string> Titles(MainViewModel viewModel) =>
        [.. viewModel.OverdueTasks.Concat(viewModel.Tasks).Select(t => t.Title)];
}
