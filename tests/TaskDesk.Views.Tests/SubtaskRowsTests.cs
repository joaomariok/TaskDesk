using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.System;
using Windows.UI.Text;

namespace TaskDesk.Views.Tests;

/// <summary>
/// A Task's Subtask rows in the one Task list over a real database: the chevron, Right and Left, the Subtask row's
/// layout and look in each group, its automation, and that it never takes the selection (yet).
/// </summary>
[TestClass]
public sealed class SubtaskRowsTests
{
    private const string ClosedGlyph = "";
    private const string OpenGlyph = "";

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_chevron_click_opens_and_closes_the_Task_and_neither_selects_it_nor_opens_the_pane()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "book hotel", "pack");
        await AddAsync(db, "other");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "trip", "other");
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            viewModel.IsDetailPaneOpen = false; // the owner closed it: only an explicit open shows it again
            var chevron = Chevron(list, "trip");
            Assert.AreEqual(ClosedGlyph, ((FontIcon)chevron.Content).Glyph);

            Click(chevron);
            await WaitForRowsAsync(window, "trip", "Subtask book hotel", "Subtask pack", "other");
            Assert.AreEqual(OpenGlyph, ((FontIcon)Chevron(list, "trip").Content).Glyph);
            Assert.AreEqual("other", viewModel.SelectedTask?.Title);
            Assert.AreEqual("other", ((ITaskListRow)list.SelectedItem).Title);
            Assert.IsFalse(viewModel.IsDetailPaneOpen, "The chevron opened the detail pane.");

            Click(Chevron(list, "trip"));
            await WaitForRowsAsync(window, "trip", "other");
            Assert.AreEqual(ClosedGlyph, ((FontIcon)Chevron(list, "trip").Content).Glyph);
            Assert.AreEqual("other", viewModel.SelectedTask?.Title);
            Assert.IsFalse(viewModel.IsDetailPaneOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Right_on_a_closed_Task_row_opens_it_and_Left_on_an_open_one_closes_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        await AddAsync(db, "other");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            await WaitForRowsAsync(window, "trip", "other");
            var taskList = window.View.TaskList;
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "trip");

            Assert.IsFalse(taskList.TryShortcut(VirtualKey.Left, VirtualKeyModifiers.None), "Left closed a closed Task.");
            Assert.IsTrue(taskList.TryShortcut(VirtualKey.Right, VirtualKeyModifiers.None));
            await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            Assert.IsFalse(taskList.TryShortcut(VirtualKey.Right, VirtualKeyModifiers.None), "Right opened an open Task.");
            Assert.AreEqual("trip", viewModel.SelectedTask?.Title);

            Assert.IsTrue(taskList.TryShortcut(VirtualKey.Left, VirtualKeyModifiers.None));
            await WaitForRowsAsync(window, "trip", "other");
            Assert.AreEqual("trip", viewModel.SelectedTask?.Title);

            // A Task without Subtasks has nothing to open.
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            Assert.IsFalse(taskList.TryShortcut(VirtualKey.Right, VirtualKeyModifiers.None));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Subtask_row_never_takes_the_selection_so_Space_still_acts_on_the_selected_Task()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.LoadAsync(Ct);
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "other");
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            viewModel.IsDetailPaneOpen = false; // the owner closed it: only an explicit open shows it again

            // As a click on the row would, then as Down from the Task row would.
            list.SelectedItem = trip.SubtaskRows[0];
            Assert.AreEqual("other", ((ITaskListRow)list.SelectedItem).Title);
            list.SelectedIndex = list.Items.IndexOf(trip.SubtaskRows[1]);
            Assert.AreEqual("other", ((ITaskListRow)list.SelectedItem).Title);
            Assert.AreEqual("other", viewModel.SelectedTask?.Title);
            Assert.IsFalse(viewModel.IsDetailPaneOpen);

            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => viewModel.CompletedTasks.Any(t => t.Title == "other"),
                () => "Space did not complete the selected Task.", Ct);
            Assert.IsFalse(trip.IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_Subtask_row_has_its_checkbox_under_the_Task_title_no_stripe_and_a_Task_rows_height()
    {
        using var db = await DatabaseAsync();
        var home = await AddListAsync(db, "Home", "#107C10");
        await AddAsync(db, "trip", ["pack"], listId: home);
        await AddAsync(db, "other", [], listId: home);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowListAsync(home, Ct);
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            var (trip, pack, other) = (Container(list, "trip"), Container(list, "Subtask pack"), Container(list, "other"));

            var tripTitle = VisualTree.Find<TextBlock>(trip, "RowTitle");
            var box = VisualTree.Descendants(pack).OfType<CheckBox>().Single();
            Assert.AreEqual(Left(tripTitle, list), Left(box, list), 0.5, "The Subtask checkbox is not under the Task title.");
            Assert.AreEqual(Left(tripTitle, list), Left(VisualTree.Find<TextBlock>(other, "RowTitle"), list), 0.5,
                "A Task without Subtasks does not leave the chevron column blank.");
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<Button>(other, "Chevron").Visibility);
            Assert.AreEqual(Visibility.Visible, VisualTree.Find<Button>(trip, "Chevron").Visibility);

            Assert.IsFalse(VisualTree.Descendants(pack).OfType<Border>().Any(b => b.Name == "ListStripe"),
                "A Subtask row draws the List stripe.");
            Assert.AreEqual(trip.ActualHeight, pack.ActualHeight, "A Subtask row is not as tall as a Task row.");
            Assert.AreEqual(32, pack.ActualHeight);
            Assert.AreEqual("pack", VisualTree.Find<TextBlock>(pack, "RowTitle").Text);
            Assert.AreEqual("Subtask done", AutomationProperties.GetName(box));

            // Nothing on the right: the title is the row's only text.
            var inBox = VisualTree.Descendants(box).ToHashSet();
            var texts = VisualTree.Descendants(pack).OfType<TextBlock>()
                .Where(t => !inBox.Contains(t) && t.Text.Length > 0 && t.ActualWidth > 0).Select(t => t.Text);
            CollectionAssert.AreEqual(new[] { "pack" }, texts.ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_ticked_Subtask_dims_and_strikes_through_its_block_dims_in_Completed_N_and_keeps_full_strength_on_the_Completed_Smart_View()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        var tasks = await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct);
        await db.Get<ITaskService>().SetSubtaskCompletedAsync(tasks.Single().Subtasks[0].Id, true, Ct);
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.LoadAsync(Ct);
            viewModel.OpenTask(viewModel.Tasks.Single());
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go");

            AssertLook(list, "Subtask pack", 0.6, struck: true, ticked: true);
            AssertLook(list, "Subtask go", 1.0, struck: false, ticked: false);

            // A tick in the detail pane shows on the row; unticked again, the row goes back to full strength.
            viewModel.OpenDetailPane(viewModel.Tasks.Single());
            viewModel.SubtaskItems[1].IsCompleted = true;
            await viewModel.PendingSave;
            await UiWait.UntilAsync(() => VisualTree.Find<Grid>(Container(list, "Subtask go"), "RowRoot").Opacity < 1,
                () => "The tick made in the pane did not dim the Subtask row.", Ct);
            AssertLook(list, "Subtask go", 0.6, struck: true, ticked: true);
            viewModel.SubtaskItems[1].IsCompleted = false;
            await viewModel.PendingSave;
            await UiWait.UntilAsync(() => VisualTree.Find<Grid>(Container(list, "Subtask go"), "RowRoot").Opacity == 1,
                () => "The untick made in the pane did not undim the Subtask row.", Ct);
            AssertLook(list, "Subtask go", 1.0, struck: false, ticked: false);

            // Completed, the Task and its rows move into "Completed (N)" and the block dims once.
            await viewModel.ToggleCompleteCommand.ExecuteAsync(viewModel.Tasks.Single());
            viewModel.IsCompletedExpanded = true;
            await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go");
            await UiWait.UntilAsync(() => viewModel.CompletedTasks.Count == 1, () => "The Task did not complete.", Ct);
            AssertLook(list, "Subtask pack", 0.6, struck: true, ticked: true);
            AssertLook(list, "Subtask go", 0.6, struck: false, ticked: false);

            await viewModel.ShowSmartViewAsync(SmartView.Completed, Ct);
            await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go");
            AssertLook(list, "Subtask pack", 1.0, struck: false, ticked: true);
            AssertLook(list, "Subtask go", 1.0, struck: false, ticked: false);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task UI_Automation_reads_a_Task_row_as_expanded_or_collapsed_and_a_Subtask_row_as_Subtask_and_its_title()
    {
        using var db = await DatabaseAsync();
        var tripId = await AddAsync(db, "trip", "pack");
        await AddAsync(db, "other");
        var tasks = await db.Get<ITaskService>().ListDefaultListTasksAsync(Ct);
        await db.Get<ITaskService>().SetSubtaskCompletedAsync(tasks.Single(t => t.Id == tripId).Subtasks[0].Id, true, Ct);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "trip", "other");
            var listPeer = FrameworkElementAutomationPeer.CreatePeerForElement(list);

            var trip = (IExpandCollapseProvider)ItemPeer(listPeer, "trip").GetPattern(PatternInterface.ExpandCollapse);
            Assert.IsNotNull(trip, "The Task row has no ExpandCollapse pattern.");
            Assert.AreEqual(ExpandCollapseState.Collapsed, trip.ExpandCollapseState);
            Assert.IsNull(ItemPeer(listPeer, "other").GetPattern(PatternInterface.ExpandCollapse),
                "A Task without Subtasks reads as expandable.");

            trip.Expand();
            await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            Assert.AreEqual(ExpandCollapseState.Expanded, trip.ExpandCollapseState);
            var subtask = ItemPeer(listPeer, "Subtask pack");
            Assert.IsNull(subtask.GetPattern(PatternInterface.ExpandCollapse));
            var box = VisualTree.Descendants(Container(list, "Subtask pack")).OfType<CheckBox>().Single();
            Assert.AreEqual(ToggleState.On,
                ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(box)
                    .GetPattern(PatternInterface.Toggle)).ToggleState);

            trip.Collapse();
            await WaitForRowsAsync(window, "trip", "other");
            Assert.AreEqual(ExpandCollapseState.Collapsed, trip.ExpandCollapseState);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_chevron_is_the_Completed_headings_12px_glyph_in_soft_ink()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.RequestedTheme = ElementTheme.Light;
            await viewModel.LoadAsync(Ct);
            var list = await WaitForRowsAsync(window, "trip");
            var glyph = (FontIcon)Chevron(list, "trip").Content;
            var soft = (await ThemeColours.ResolveAsync(ElementTheme.Light, ["TextFillColorSecondaryBrush"], Ct))
                ["TextFillColorSecondaryBrush"];

            Assert.AreEqual(12, glyph.FontSize);
            Assert.AreEqual(soft, ((SolidColorBrush)glyph.Foreground).Color);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertLook(ListView list, string name, double opacity, bool struck, bool ticked)
    {
        var row = Container(list, name);
        Assert.AreEqual(opacity, VisualTree.Find<Grid>(row, "RowRoot").Opacity, 0.001, $"{name}: opacity");
        Assert.AreEqual(struck, VisualTree.Find<TextBlock>(row, "RowTitle").TextDecorations
            == TextDecorations.Strikethrough, $"{name}: strike-through");
        Assert.AreEqual(ticked, VisualTree.Descendants(row).OfType<CheckBox>().Single().IsChecked, $"{name}: tick");
    }

    private static void Click(Button button) =>
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)
            .GetPattern(PatternInterface.Invoke)).Invoke();

    private static double Left(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

    // The automation peer a UI Automation client finds for a row of the list, by its name.
    private static AutomationPeer ItemPeer(AutomationPeer root, string name)
    {
        foreach (var child in root.GetChildren() ?? [])
        {
            if (child.GetName() == name && child.GetAutomationControlType() == AutomationControlType.ListItem)
            {
                return child;
            }

            if (child.GetAutomationControlType() != AutomationControlType.ListItem
                && FindOrNull(child, name) is { } found)
            {
                return found;
            }
        }

        Assert.Fail($"No list item '{name}' in the automation tree.");
        return null!;

        static AutomationPeer? FindOrNull(AutomationPeer peer, string name)
        {
            foreach (var child in peer.GetChildren() ?? [])
            {
                if (child.GetName() == name && child.GetAutomationControlType() == AutomationControlType.ListItem)
                {
                    return child;
                }

                if (FindOrNull(child, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    private static Button Chevron(ListView list, string name) => VisualTree.Find<Button>(Container(list, name), "Chevron");

    // The shown row containers of the list, in order; containers kept for recycling hold no item.
    private static IEnumerable<ListViewItem> ShownRows(ListView list) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer);

    private static ListViewItem Container(ListView list, string name) =>
        ShownRows(list).Single(c => AutomationProperties.GetName(c) == name);

    // Waits until the Task list shows exactly these rows (by their accessible names), and returns it.
    private async Task<ListView> WaitForRowsAsync(MainWindow window, params string[] names)
    {
        var list = VisualTree.Find<ListView>(window.View, "OpenTaskList");
        List<string> Shown() => [.. ShownRows(list).Select(AutomationProperties.GetName)];
        await UiWait.UntilAsync(() => Shown().SequenceEqual(names),
            () => $"Expected [{string.Join(", ", names)}], was [{string.Join(", ", Shown())}].", Ct);
        return list;
    }

    private (MainWindow Window, MainViewModel ViewModel) Open(TempDatabase db)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        window.Activate();
        return (window, viewModel);
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    private async Task<Guid> AddListAsync(TempDatabase db, string name, string colour)
    {
        var list = await db.Get<IListService>().CreateListAsync(name, Ct);
        await db.Get<IListService>().SetListColorAsync(list.Id, colour, Ct);
        return list.Id;
    }

    private Task<Guid> AddAsync(TempDatabase db, string title, params string[] subtasks) =>
        AddAsync(db, title, subtasks, null);

    // A Task in the Default List, or in listId's List, with its Subtasks in this order.
    private async Task<Guid> AddAsync(TempDatabase db, string title, string[] subtasks, Guid? listId = null)
    {
        var service = db.Get<ITaskService>();
        var task = listId is { } id
            ? await db.Get<ITaskCreator>().CreateTaskInListAsync(title, id, Ct)
            : await service.CreateTaskAsync(title, Ct);
        foreach (var subtask in subtasks)
        {
            await service.AddSubtaskAsync(task.Id, subtask, Ct);
        }

        return task.Id;
    }
}
