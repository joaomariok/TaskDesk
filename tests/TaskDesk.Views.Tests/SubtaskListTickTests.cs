using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
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
/// Ticking a Subtask from its row in the Task list over a real database: its checkbox ticks it without selecting
/// the row or opening the pane, and Space on a selected Subtask row does the same.
/// </summary>
[TestClass]
public sealed class SubtaskListTickTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [UITestMethod]
    public async Task A_checkbox_click_on_a_Subtask_row_ticks_it_without_selecting_it_or_opening_the_pane()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack", "go");
        await AddAsync(db, "other");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            var trip = viewModel.Tasks.Single(t => t.Title == "trip");
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack", "Subtask go", "other");
            viewModel.SelectedTask = viewModel.Tasks.Single(t => t.Title == "other");
            viewModel.IsDetailPaneOpen = false;

            var box = VisualTree.Descendants(Container(list, "Subtask go")).OfType<CommandCheckBox>().Single();
            ((ToggleButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(box)).Toggle();

            await UiWait.UntilAsync(() => box.IsChecked == true, () => "The box did not show the tick.", Ct);
            var stored = (await db.QueryAsync(c => c.Subtasks.OrderBy(s => s.SortOrder))).ToList();
            Assert.IsFalse(stored[0].IsCompleted);
            Assert.IsTrue(stored[1].IsCompleted);
            Assert.AreEqual("1/2", trip.SubtaskProgress);
            Assert.AreEqual("other", viewModel.SelectedTask?.Title, "The click moved the selection.");
            Assert.AreEqual("other", ((ITaskListRow)list.SelectedItem).Title);
            Assert.IsFalse(viewModel.IsDetailPaneOpen, "The click opened the detail pane.");
            Assert.IsFalse(trip.IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Space_on_a_selected_Subtask_row_ticks_and_unticks_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "trip", "pack");
        var (window, viewModel) = await OpenAsync(db, width: 1000);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            var trip = viewModel.Tasks.Single();
            viewModel.OpenTask(trip);
            var list = await WaitForRowsAsync(window, "trip", "Subtask pack");
            list.SelectedItem = trip.SubtaskRows[0];
            var box = VisualTree.Descendants(Container(list, "Subtask pack")).OfType<CommandCheckBox>().Single();

            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => box.IsChecked == true, () => "Space did not tick.", Ct);
            Assert.IsTrue((await db.QueryAsync(c => c.Subtasks)).Single().IsCompleted);
            Assert.AreSame(trip.SubtaskRows[0], viewModel.SelectedRow);
            Assert.IsFalse(trip.IsCompleted);

            Assert.IsTrue(window.View.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));
            await UiWait.UntilAsync(() => box.IsChecked == false, () => "Space did not untick.", Ct);
            Assert.IsFalse((await db.QueryAsync(c => c.Subtasks)).Single().IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

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
