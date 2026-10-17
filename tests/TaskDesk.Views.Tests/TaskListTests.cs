using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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

namespace TaskDesk.Views.Tests;

/// <summary>
/// The Task list area of the main window over a real database: the Overdue, open and "Completed (N)" groups,
/// the Completed Smart View, completing with the completion hold, Space, Delete, the Undo InfoBar and Ctrl+Z.
/// </summary>
[TestClass]
public sealed class TaskListTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_List_shows_its_open_Tasks_and_its_completed_ones_under_Completed_N()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;

            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent");
            var completed = VisualTree.Descendants(view).OfType<Expander>().Single();
            Assert.AreEqual(Visibility.Visible, completed.Visibility);
            Assert.AreEqual("Completed (1)", completed.Header);
            completed.IsExpanded = true;
            var list = await WaitForRowsAsync(view, "CompletedTaskList", "Book flights");
            Assert.AreEqual("Completed tasks", AutomationProperties.GetName(list));
            Assert.AreEqual("Tasks", AutomationProperties.GetName(VisualTree.Find<ListView>(view, "OpenTaskList")));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Today_view_shows_Overdue_Tasks_in_their_own_group_under_a_red_heading()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Renew passport", due: today.AddDays(-2));
        await AddAsync(db, "Water plants", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            window.View.RequestedTheme = ElementTheme.Light;
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            var view = window.View;

            await WaitForRowsAsync(view, "OverdueTaskList", "Renew passport");
            await WaitForRowsAsync(view, "OpenTaskList", "Water plants");
            var heading = VisualTree.Find<TextBlock>(view, "OverdueHeading");
            Assert.AreEqual("Overdue (1)", heading.Text);
            var red = (await ThemeColours.ResolveAsync(ElementTheme.Light, ["OverdueBrush"], Ct))["OverdueBrush"];
            Assert.AreEqual(red, ((SolidColorBrush)heading.Foreground).Color);
            Assert.AreEqual("Overdue tasks", AutomationProperties.GetName(VisualTree.Find<ListView>(view, "OverdueTaskList")));
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Descendants(view).OfType<Expander>().Single().Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Only_one_Task_list_has_a_selection_and_it_is_the_selected_Task()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Renew passport", due: today.AddDays(-2));
        await AddAsync(db, "Water plants", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            var overdue = await WaitForRowsAsync(window.View, "OverdueTaskList", "Renew passport");
            var open = await WaitForRowsAsync(window.View, "OpenTaskList", "Water plants");

            overdue.SelectedIndex = 0;
            Assert.AreEqual("Renew passport", viewModel.SelectedTask?.Title);

            open.SelectedIndex = 0;
            Assert.AreEqual("Water plants", viewModel.SelectedTask?.Title);
            Assert.IsNull(overdue.SelectedItem, "The Overdue list kept its selection.");

            viewModel.SelectedTask = viewModel.OverdueTasks.Single();
            Assert.AreSame(viewModel.OverdueTasks.Single(), overdue.SelectedItem);
            Assert.IsNull(open.SelectedItem, "The open list kept its selection.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Completed_Smart_View_is_a_plain_list_of_completed_Tasks_at_full_strength()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Completed, Ct);
            var view = window.View;

            var list = await WaitForRowsAsync(view, "OpenTaskList", "Book flights");
            var row = VisualTree.Descendants(list).OfType<TaskRow>().Single();
            Assert.AreEqual(1.0, VisualTree.Find<Grid>(row, "RowRoot").Opacity, 0.001);
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Descendants(view).OfType<Expander>().Single().Visibility);
            Assert.AreEqual(Visibility.Collapsed, VisualTree.Find<TextBlock>(view, "OverdueHeading").Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_row_checkbox_Toggle_completes_the_Task_after_the_completion_hold()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Water plants");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => true;
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForRowsAsync(view, "OpenTaskList", "Water plants");

            Toggle(VisualTree.Descendants(list).OfType<CheckBox>().Single());

            // Held in place, ticked, dimmed and struck through.
            await UiWait.UntilAsync(() => viewModel.Tasks.Single().IsCompleted, () => "The Task was not completed.", Ct);
            var row = VisualTree.Descendants(list).OfType<TaskRow>().Single();
            Assert.IsTrue(VisualTree.Descendants(row).OfType<CheckBox>().Single().IsChecked);
            Assert.AreEqual(0.6, VisualTree.Find<Grid>(row, "RowRoot").Opacity, 0.001);
            CollectionAssert.AreEqual(new[] { "Water plants" }, RowNames(list));

            db.Clock.Advance(CompletionHold.For(animationsEnabled: true));

            await WaitForRowsAsync(view, "OpenTaskList");
            Assert.AreEqual("Completed (1)", VisualTree.Descendants(view).OfType<Expander>().Single().Header);
            Assert.AreEqual(1, (await db.QueryAsync(c => c.Tasks.Where(t => t.IsCompleted))).Count);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task With_Windows_animations_off_a_completed_Task_moves_at_once()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Water plants");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForRowsAsync(view, "OpenTaskList", "Water plants");

            Toggle(VisualTree.Descendants(list).OfType<CheckBox>().Single());

            // The fake clock never moves: no hold to wait out.
            await WaitForRowsAsync(view, "OpenTaskList");
            Assert.AreEqual("Completed (1)", VisualTree.Descendants(view).OfType<Expander>().Single().Header);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Space_completes_the_selected_Task()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Water plants");
        await AddAsync(db, "Pay rent");
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForRowsAsync(view, "OpenTaskList", "Water plants", "Pay rent");
            list.SelectedIndex = 0;

            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));

            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent");
            Assert.IsTrue(viewModel.CompletedTasks.Single().Title == "Water plants");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Delete_removes_the_selected_Task_and_the_Undo_InfoBar_restores_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Renew passport");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForRowsAsync(view, "OpenTaskList", "Renew passport");
            var bar = VisualTree.Find<InfoBar>(view, "UndoBar");
            Assert.IsFalse(bar.IsOpen, "The Undo bar shows before anything was deleted.");
            list.SelectedIndex = 0;

            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Delete, VirtualKeyModifiers.None));

            await WaitForRowsAsync(view, "OpenTaskList");
            await UiWait.UntilAsync(() => bar.IsOpen, () => "The Undo bar did not open.", Ct);
            Assert.AreEqual("Task deleted", AutomationProperties.GetName(bar));
            Assert.AreEqual("Task deleted", bar.Message);
            Assert.IsFalse(bar.IsClosable);
            var undo = (Button)bar.ActionButton;
            Assert.AreEqual("Undo", undo.Content);
            Assert.AreEqual("UndoButton", AutomationProperties.GetAutomationId(undo));

            ((ButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(undo)).Invoke();

            await WaitForRowsAsync(view, "OpenTaskList", "Renew passport");
            await UiWait.UntilAsync(() => !bar.IsOpen, () => "The Undo bar stayed open after Undo.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Delete_then_Ctrl_Z_restores_the_Task()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Renew passport");
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForRowsAsync(view, "OpenTaskList", "Renew passport");
            list.SelectedIndex = 0;
            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Delete, VirtualKeyModifiers.None));
            await WaitForRowsAsync(view, "OpenTaskList");

            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Z, VirtualKeyModifiers.Control));

            await WaitForRowsAsync(view, "OpenTaskList", "Renew passport");
            Assert.IsTrue(
                view.TaskList.KeyboardAccelerators.Any(a => a.Key == VirtualKey.Z && a.Modifiers == VirtualKeyModifiers.Control),
                "Ctrl+Z is not a keyboard accelerator of the Task list area.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task An_empty_Task_list_takes_no_keyboard_focus_and_a_list_with_rows_does()
    {
        using var db = await DatabaseAsync();
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = VisualTree.Find<ListView>(view, "OpenTaskList");
            Assert.IsFalse(list.IsTabStop, "The empty Task list takes keyboard focus.");

            viewModel.QuickAddTitle = "Pay rent";
            await viewModel.AddTaskCommand.ExecuteAsync(null);

            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent");
            Assert.IsTrue(list.IsTabStop, "The Task list with a row takes no keyboard focus.");
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Task_lists_draw_no_border_or_background_and_rows_stay_one_compact_line()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Renew passport", due: today.AddDays(-2));
        await AddAsync(db, "Water plants", due: today);
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            await WaitForRowsAsync(window.View, "OpenTaskList", "Water plants");
            await viewModel.ShowSmartViewAsync(null, Ct);
            VisualTree.Descendants(window.View).OfType<Expander>().Single().IsExpanded = true;
            var open = await WaitForRowsAsync(window.View, "OpenTaskList", "Renew passport", "Water plants");
            await WaitForRowsAsync(window.View, "CompletedTaskList", "Book flights");

            foreach (var name in new[] { "OverdueTaskList", "OpenTaskList", "CompletedTaskList" })
            {
                var list = VisualTree.Find<ListView>(window.View, name);
                Assert.AreEqual(new Thickness(0), list.BorderThickness, $"{name} draws a border.");
                Assert.IsTrue(list.Background is null or SolidColorBrush { Color.A: 0 }, $"{name} draws a background.");
            }

            // A row with a Due chip is the tallest kind; Fluent's own list item is 40 px tall.
            foreach (var row in VisualTree.Descendants(open).OfType<ListViewItem>())
            {
                Assert.AreEqual(32, row.ActualHeight, $"Row '{AutomationProperties.GetName(row)}' is not one compact line.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void Toggle(CheckBox check) =>
        ((ToggleButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(check)).Toggle();

    private static List<string> RowNames(ListView list) =>
        [.. VisualTree.Descendants(list).OfType<ListViewItem>().Select(AutomationProperties.GetName)];

    // Waits until the named Task list shows exactly these rows (by their accessible names), and returns it.
    private async Task<ListView> WaitForRowsAsync(MainView view, string name, params string[] titles)
    {
        // A list inside a just-expanded Expander joins the visual tree a moment later.
        ListView? Shown() => VisualTree.Descendants(view).OfType<ListView>().FirstOrDefault(l => l.Name == name);
        await UiWait.UntilAsync(() => Shown() is { } list && RowNames(list).SequenceEqual(titles),
            () => Shown() is { } list
                ? $"{name}: expected [{string.Join(", ", titles)}], was [{string.Join(", ", RowNames(list))}]."
                : $"No {name} is shown.", Ct);
        return Shown()!;
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

    private static async Task AddAsync(TempDatabase db, string title, bool completed = false, DateOnly? due = null)
    {
        var listId = (await db.QueryAsync(c => c.Lists.Where(l => l.IsDefault))).Single().Id;
        await using var context = await db.Get<IDbContextFactory<TaskDeskDbContext>>().CreateDbContextAsync();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var order = await context.Tasks.CountAsync();
        context.Tasks.Add(new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ListId = listId,
            Title = title,
            IsCompleted = completed,
            CompletedUtc = completed ? now : null,
            DueDate = due,
            SortOrder = order,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await context.SaveChangesAsync();
    }
}
