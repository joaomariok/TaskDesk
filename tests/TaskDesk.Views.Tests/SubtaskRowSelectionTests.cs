using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using TaskDesk.Core.Lists;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;
using TaskDesk.Data;
using TaskDesk.Data.Tests;
using TaskDesk.Presentation;
using Windows.System;

namespace TaskDesk.Views.Tests;

/// <summary>
/// Subtask rows as rows of the one Task list over a real database: they take the list's one selection, which
/// stands for their Task (the docked pane, Enter, a click, F2), Left selects the Task, and Esc returns focus to the
/// selected Subtask row.
/// </summary>
[TestClass]
public sealed class SubtaskRowSelectionTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task Up_and_Down_walk_through_Subtask_rows_and_Task_rows_across_groups_with_one_selection()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var done = await AddAsync(db, "done", "d1");
        await db.Get<ITaskService>().SetCompletedAsync(done, true, Ct);
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            viewModel.IsCompletedExpanded = true;
            viewModel.OpenTask(viewModel.Tasks.Single(t => t.Title == "trip"));
            viewModel.OpenTask(viewModel.CompletedTasks.Single());
            var list = await WaitForRowsAsync(window,
                "trip", "Subtask pack", "Subtask go", "other", "done", "Subtask d1");

            // As Down then Up would: every row in turn, the list's one selection and the view model's together.
            int[] walk = [0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 0];
            foreach (var index in walk)
            {
                list.SelectedIndex = index;
                var row = (ITaskListRow)list.Items[index];
                Assert.AreSame(row, viewModel.SelectedRow, $"Row {index}: the view model's selection.");
                Assert.AreSame(row.TaskRow, viewModel.SelectedTask, $"Row {index}: the selected Task.");
                Assert.AreEqual(1, list.SelectedItems.Count, $"Row {index}: one selection.");
                Assert.AreSame(row, list.SelectedItem, $"Row {index}: the list's selection.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Enter_or_a_click_on_a_Subtask_row_opens_the_pane_on_its_Task_and_its_checkbox_does_not()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "other");
            var taskList = window.View.TaskList;
            var title = VisualTree.Find<TextBox>(window.Details.Pane, "DetailTitleBox");
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            viewModel.IsDetailPaneOpen = false;

            list.SelectedItem = trip.SubtaskRows[1];
            Assert.IsTrue(taskList.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));
            Assert.IsTrue(viewModel.IsDetailPaneVisible, "Enter did not open the pane.");
            Assert.AreSame(trip, viewModel.DetailTask);
            Assert.AreSame(trip.SubtaskRows[1], viewModel.SelectedRow);
            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), title),
                () => "Enter did not move focus to the title.", Ct);

            // A click on the Subtask row's checkbox opens nothing; a click on the row opens its Task.
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            viewModel.IsDetailPaneOpen = false;
            var pack = Container(list, "Subtask pack");
            Assert.IsFalse(taskList.OpenRowAt(VisualTree.Descendants(pack).OfType<CheckBox>().Single()));
            Assert.IsFalse(viewModel.IsDetailPaneOpen, "A click on the checkbox opened the pane.");
            var text = VisualTree.Descendants(pack).OfType<TextBlock>().First(t => t.Text == "pack");
            Assert.IsTrue(taskList.OpenRowAt(text));
            Assert.IsTrue(viewModel.IsDetailPaneVisible, "The click did not open the pane.");
            Assert.AreSame(trip, viewModel.DetailTask);
            Assert.AreSame(trip.SubtaskRows[0], viewModel.SelectedRow);
            Assert.AreSame(trip.SubtaskRows[0], list.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_docked_pane_follows_a_selected_Subtask_row_to_its_Task_and_F2_edits_the_Tasks_title()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1200);
        try
        {
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            var title = VisualTree.Find<TextBox>(window.Details.Pane, "DetailTitleBox");
            viewModel.OpenDetailPane(viewModel.Tasks.Single(t => t.Title == "other"));
            Assert.IsTrue(viewModel.IsDetailDocked, "The pane is not docked.");

            list.SelectedIndex = 1;
            await UiWait.UntilAsync(() => title.Text == "trip",
                () => $"The docked pane shows '{title.Text}', not the Subtask row's Task.", Ct);
            Assert.AreSame(trip, viewModel.DetailTask);

            // Closed, F2 opens it on the Task and edits its title; the Subtask row stays selected.
            viewModel.IsDetailPaneOpen = false;
            Assert.IsTrue(window.Details.TryShortcut(VirtualKey.F2, VirtualKeyModifiers.None));
            Assert.IsTrue(viewModel.IsDetailPaneVisible);
            Assert.AreEqual("trip", title.Text);
            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), title),
                () => "F2 did not move focus to the title.", Ct);
            Assert.AreSame(trip.SubtaskRows[0], viewModel.SelectedRow);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Left_on_a_Subtask_row_selects_and_focuses_its_Task_and_closing_the_Task_selects_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "other");
            var taskList = window.View.TaskList;

            list.SelectedItem = trip.SubtaskRows[1];
            Container(list, "Subtask go").Focus(FocusState.Keyboard);
            Assert.IsFalse(taskList.TryShortcut(VirtualKey.Right, VirtualKeyModifiers.None), "Right did something.");
            Assert.IsTrue(taskList.TryShortcut(VirtualKey.Left, VirtualKeyModifiers.None));
            Assert.AreSame(trip, viewModel.SelectedRow);
            Assert.AreSame(trip, list.SelectedItem);
            Assert.IsTrue(trip.IsOpen, "Left on a Subtask row closed its Task.");
            await UiWait.UntilAsync(() => FocusedRowName(window) == "trip",
                () => $"Focus is on '{FocusedRowName(window)}', not the Task's row.", Ct);

            // Closing the Task while one of its Subtask rows is selected selects the Task, and its row takes the focus.
            list.SelectedItem = trip.SubtaskRows[0];
            Container(list, "Subtask pack").Focus(FocusState.Keyboard);
            viewModel.CloseTask(trip);
            await WaitForRowsAsync(window, "trip", "other");
            Assert.AreSame(trip, viewModel.SelectedRow);
            Assert.AreSame(trip, list.SelectedItem);
            await UiWait.UntilAsync(() => FocusedRowName(window) == "trip",
                () => $"Focus is on '{FocusedRowName(window)}', not the closed Task's row.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Closing_a_Task_while_focus_is_inside_one_of_its_Subtask_rows_focuses_the_Tasks_row()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "other");

            // The focus is on the row's own box, not on the row: by the time the selection reaches the Task, the
            // focus is no longer on a row of the list, as when the removed row's focus has already moved on.
            list.SelectedItem = trip.SubtaskRows[0];
            Assert.IsTrue(VisualTree.Find<CheckBox>(Container(list, "Subtask pack"), "TickedBox")
                .Focus(FocusState.Keyboard), "The Subtask row's box took no focus.");
            viewModel.CloseTask(trip);
            await WaitForRowsAsync(window, "trip", "other");
            Assert.AreSame(trip, viewModel.SelectedRow);
            await UiWait.UntilAsync(() => FocusedRowName(window) == "trip",
                () => $"Focus is on '{FocusedRowName(window)}', not the closed Task's row.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Closing_a_Task_whose_Subtask_row_is_selected_leaves_a_focus_outside_the_list_where_it_is()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go");

            list.SelectedItem = trip.SubtaskRows[0];
            var search = VisualTree.Find<TextBox>(window.View, "SearchBox");
            Assert.IsTrue(search.Focus(FocusState.Keyboard), "The search box took no focus.");
            viewModel.CloseTask(trip);
            await WaitForRowsAsync(window, "trip");
            Assert.AreSame(trip, viewModel.SelectedRow);
            // A low-priority round trip runs after any focus move the close enqueued at normal priority.
            var drained = new TaskCompletionSource();
            Assert.IsTrue(window.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, drained.SetResult));
            await drained.Task.WaitAsync(Ct);
            Assert.AreSame(search, FocusManager.GetFocusedElement(window.Content.XamlRoot),
                "Closing the Task took the focus from the search box.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Esc_closes_the_pane_and_returns_focus_to_the_selected_Subtask_row()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            var trip = viewModel.Tasks.Single();
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go");
            list.SelectedItem = trip.SubtaskRows[1];
            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Enter, VirtualKeyModifiers.None));

            Assert.IsTrue(window.Details.TryShortcut(VirtualKey.Escape, VirtualKeyModifiers.None));
            Assert.IsFalse(viewModel.IsDetailPaneVisible, "Esc did not close the pane.");
            await UiWait.UntilAsync(() => FocusedRowName(window) == "Subtask go",
                () => $"Esc returned focus to '{FocusedRowName(window)}'.", Ct);
            Assert.AreSame(trip.SubtaskRows[1], viewModel.SelectedRow);
            Assert.AreSame(trip.SubtaskRows[1], list.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Delete_and_Space_on_a_selected_Subtask_row_change_nothing()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            list.SelectedItem = trip.SubtaskRows[0];

            window.View.TaskList.TryShortcut(VirtualKey.Delete, VirtualKeyModifiers.None);
            window.View.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None);
            await viewModel.CompletionMove;

            await WaitForRowsAsync(window, "trip", "Subtask pack", "other");
            var stored = await db.QueryAsync(c => c.Tasks);
            Assert.AreEqual(2, stored.Count);
            Assert.IsFalse(stored.Any(t => t.IsCompleted), "Space completed a Task.");
            Assert.IsFalse(viewModel.IsUndoBarVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task A_selected_Subtask_row_hidden_in_a_collapsed_Completed_N_is_selected_again_when_it_shows()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "other");
        var done = await AddAsync(db, "done", "d1");
        await db.Get<ITaskService>().SetCompletedAsync(done, true, Ct);
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            viewModel.IsCompletedExpanded = true;
            var row = viewModel.CompletedTasks.Single();
            viewModel.OpenTask(row);
            var list = await WaitForRowsAsync(window, "other", "done", "Subtask d1");
            list.SelectedItem = row.SubtaskRows[0];

            viewModel.IsCompletedExpanded = false;
            await WaitForRowsAsync(window, "other");
            Assert.AreSame(row.SubtaskRows[0], viewModel.SelectedRow);
            Assert.IsNull(list.SelectedItem);

            viewModel.IsCompletedExpanded = true;
            await WaitForRowsAsync(window, "other", "done", "Subtask d1");
            await UiWait.UntilAsync(() => ReferenceEquals(list.SelectedItem, row.SubtaskRows[0]),
                () => $"The list selected {(list.SelectedItem as ITaskListRow)?.Title ?? "nothing"}.", Ct);
            Assert.AreSame(row.SubtaskRows[0], viewModel.SelectedRow);
        }
        finally
        {
            window.Close();
        }
    }

    private static string? FocusedRowName(MainWindow window) =>
        FocusManager.GetFocusedElement(window.Content.XamlRoot) is ListViewItem item
            ? AutomationProperties.GetName(item)
            : null;

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

    // A window whose content is `width` px wide (docked pane above 1100 px), loaded.
    private async Task<(MainWindow Window, MainViewModel ViewModel)> OpenAsync(TempDatabase db, double width)
    {
        var viewModel = new MainViewModel(
            db.Get<ITaskService>(), db.Get<ITagService>(), db.Clock,
            dueService: db.Get<IDueService>(), searchService: db.Get<ITaskSearchService>(),
            listService: db.Get<IListService>());
        var window = new MainWindow(viewModel);
        try
        {
            window.Activate();
            await UiWait.UntilAsync(() => window.Content is FrameworkElement { IsLoaded: true },
                () => "The window did not load.", Ct);
            var content = (FrameworkElement)window.Content;
            var scale = content.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)Math.Round(width * scale), (int)Math.Round(600 * scale)));
            await UiWait.UntilAsync(() => Math.Abs(content.ActualWidth - width) < 1,
                () => $"The content is {content.ActualWidth} px wide, not {width}.", Ct);
            content.UpdateLayout();
            await viewModel.LoadAsync(Ct);
            return (window, viewModel);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private async Task<TempDatabase> DatabaseAsync()
    {
        var db = new TempDatabase();
        await db.Get<IDatabaseInitializer>().InitializeAsync(Ct);
        return db;
    }

    // A Task in the Default List with its Subtasks in this order.
    private async Task<Guid> AddAsync(TempDatabase db, string title, params string[] subtasks)
    {
        var service = db.Get<ITaskService>();
        var task = await service.CreateTaskAsync(title, Ct);
        foreach (var subtask in subtasks)
        {
            await service.AddSubtaskAsync(task.Id, subtask, Ct);
        }

        return task.Id;
    }
}
