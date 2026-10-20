using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

            await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Pay rent");
            var completed = await CompletedHeadingAsync(view);
            Assert.AreEqual(Visibility.Visible, completed.Visibility);
            Assert.AreEqual("Completed (1)", AutomationProperties.GetName(completed));
            completed.IsExpanded = true;
            var list = await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");
            Assert.AreEqual("Completed tasks", GroupName(list, TaskGroupKind.Completed));
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

            var list = await WaitForGroupRowsAsync(view, TaskGroupKind.Overdue, "Renew passport");
            await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Water plants");
            var heading = VisualTree.Find<TextBlock>(view, "OverdueHeading");
            Assert.AreEqual("Overdue (1)", heading.Text);
            var red = (await ThemeColours.ResolveAsync(ElementTheme.Light, ["OverdueBrush"], Ct))["OverdueBrush"];
            Assert.AreEqual(red, ((SolidColorBrush)heading.Foreground).Color);
            Assert.AreEqual("Overdue tasks", GroupName(list, TaskGroupKind.Overdue));
            Assert.AreEqual(Visibility.Collapsed, HeadingVisibility<CollapsibleHeading>(view));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_list_selection_is_the_selected_Task_in_every_group()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Renew passport", due: today.AddDays(-2));
        await AddAsync(db, "Water plants", due: today);
        await AddAsync(db, "Book flights", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            var list = await WaitForGroupRowsAsync(window.View, TaskGroupKind.Open, "Water plants", "Book flights");
            await viewModel.ToggleCompleteCommand.ExecuteAsync(viewModel.Tasks.Single(t => t.Title == "Book flights"));
            (await CompletedHeadingAsync(window.View)).IsExpanded = true;
            await WaitForGroupRowsAsync(window.View, TaskGroupKind.Completed, "Book flights");

            // From the list to the view model, in each group: one selection across the list.
            foreach (var title in new[] { "Renew passport", "Water plants", "Book flights" })
            {
                list.SelectedItem = Row(list, title);
                Assert.AreEqual(title, viewModel.SelectedTask?.Title);
                Assert.AreEqual(1, list.SelectedItems.Count, "The list holds more than one selection.");
            }

            // From the view model to the list, in each group.
            foreach (var row in new[] { viewModel.OverdueTasks.Single(), viewModel.Tasks.Single(), viewModel.CompletedTasks.Single() })
            {
                viewModel.SelectedTask = row;
                Assert.AreSame(row, list.SelectedItem);
                Assert.AreEqual(1, list.SelectedItems.Count, "The list holds more than one selection.");
            }
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
            Assert.AreEqual(Visibility.Collapsed, HeadingVisibility<CollapsibleHeading>(view));
            Assert.AreEqual(Visibility.Collapsed, HeadingVisibility<TextBlock>(view, "OverdueHeading"));
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
            Assert.AreEqual("Completed (1)", AutomationProperties.GetName(await CompletedHeadingAsync(view)));
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
            Assert.AreEqual("Completed (1)", AutomationProperties.GetName(await CompletedHeadingAsync(view)));
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
            await WaitForGroupRowsAsync(window.View, TaskGroupKind.Open, "Water plants");
            await viewModel.ShowSmartViewAsync(null, Ct);
            (await CompletedHeadingAsync(window.View)).IsExpanded = true;
            var open = await WaitForGroupRowsAsync(window.View, TaskGroupKind.Open, "Renew passport", "Water plants");
            await WaitForGroupRowsAsync(window.View, TaskGroupKind.Completed, "Book flights");

            // The one list holds the Overdue, open and completed groups.
            foreach (var name in new[] { "OpenTaskList" })
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

    [UITestMethod]
    public async Task On_Today_every_Overdue_Task_is_a_row_of_the_one_list_with_no_height_cap()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        var titles = Enumerable.Range(1, 8).Select(i => $"Late {i}").ToArray();
        foreach (var title in titles)
        {
            await AddAsync(db, title, due: today.AddDays(-3));
        }

        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            var list = await WaitForRowsAsync(window.View, "OpenTaskList", titles);

            // One list in the page's own scroll: no second list, and no cap on the one there is.
            Assert.AreEqual(1, VisualTree.Descendants(window.View.TaskList).OfType<ListView>().Count());
            Assert.AreEqual(double.PositiveInfinity, list.MaxHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Completed_N_follows_the_last_open_row_starts_collapsed_and_its_heading_expands_and_collapses_it()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Water plants");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var list = await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Pay rent", "Water plants");
            var heading = await CompletedHeadingAsync(view);

            // Right under the last open row (no more than a small spacing apart), and collapsed.
            var gap = Top(heading, list) - Bottom(ShownRows(list).Last(), list);
            Assert.IsTrue(gap is >= 0 and <= 8, $"The Completed heading is {gap} px under the last open row.");
            var peer = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(heading)
                .GetPattern(PatternInterface.ExpandCollapse);
            Assert.AreEqual(ExpandCollapseState.Collapsed, peer.ExpandCollapseState);
            Assert.AreEqual("Completed (1)", AutomationProperties.GetName(heading));
            Assert.AreEqual("CompletedHeading", AutomationProperties.GetAutomationId(heading));
            CollectionAssert.AreEqual(new[] { "Pay rent", "Water plants" }, RowNames(list));

            // A click expands it: the completed rows follow in the same list.
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(heading)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");
            Assert.AreEqual(ExpandCollapseState.Expanded, peer.ExpandCollapseState);
            Assert.IsTrue(viewModel.IsCompletedExpanded);

            // A second click collapses it again.
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(heading)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent", "Water plants");
            Assert.AreEqual(ExpandCollapseState.Collapsed, peer.ExpandCollapseState);
            Assert.IsFalse(viewModel.IsCompletedExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Completed_heading_keeps_the_keyboard_focus_while_it_expands_and_collapses()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Pay rent");
            var heading = await CompletedHeadingAsync(view);
            Assert.IsTrue(heading.Focus(FocusState.Keyboard), "The Completed heading took no keyboard focus.");

            // Space and Enter on a focused button invoke it, as UI Automation's Invoke does.
            var invoke = (IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(heading)
                .GetPattern(PatternInterface.Invoke);
            invoke.Invoke();
            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");
            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), heading),
                () => $"Expanding moved the focus to {Focused(window)}.", Ct);

            invoke.Invoke();
            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent");
            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), heading),
                () => $"Collapsing moved the focus to {Focused(window)}.", Ct);
            Assert.IsFalse(viewModel.IsCompletedExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Tab_reaches_the_Completed_heading_when_the_page_has_only_collapsed_completed_Tasks()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var heading = await CompletedHeadingAsync(view);
            Assert.IsTrue(VisualTree.Find<TextBox>(view, "QuickAddBox").Focus(FocusState.Keyboard),
                "Quick add took no keyboard focus.");

            FocusManager.TryMoveFocus(FocusNavigationDirection.Next,
                new FindNextElementOptions { SearchRoot = window.Content.XamlRoot.Content });

            await UiWait.UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), heading),
                () => $"Tab from quick add moved the focus to {Focused(window)}.", Ct);
        }
        finally
        {
            window.Close();
        }
    }

    private static string Focused(MainWindow window) =>
        FocusManager.GetFocusedElement(window.Content.XamlRoot) switch
        {
            null => "nothing",
            FrameworkElement element => $"{element.GetType().Name} '{element.Name}' ({AutomationProperties.GetName(element)})",
            var other => other.GetType().Name,
        };

    [UITestMethod]
    public async Task On_Today_Overdue_N_heads_the_list_and_the_open_rows_follow_with_no_heading_or_gap()
    {
        using var db = await DatabaseAsync();
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().Date);
        await AddAsync(db, "Renew passport", due: today.AddDays(-2));
        await AddAsync(db, "Pay rent", due: today.AddDays(-1));
        await AddAsync(db, "Water plants", due: today);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Today, Ct);
            var view = window.View;
            var list = await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Water plants");
            await WaitForGroupRowsAsync(view, TaskGroupKind.Overdue, "Renew passport", "Pay rent");

            // The heading comes first; headings are not items of the list, so only the Tasks are.
            var heading = VisualTree.Find<TextBlock>(view, "OverdueHeading");
            var rows = ShownRows(list).ToList();
            Assert.IsTrue(Bottom(heading, list) <= Top(rows[0], list), "The Overdue heading is not above its rows.");
            Assert.AreEqual(3, list.Items.Count);
            CollectionAssert.AreEqual(new[] { "Renew passport", "Pay rent", "Water plants" }, RowNames(list));

            // The open group has no heading of its own and starts right under the last Overdue row.
            Assert.AreEqual(Bottom(rows[1], list), Top(rows[2], list), 0.5);
            Assert.AreEqual(1, VisualTree.Descendants(list).OfType<TextBlock>().Count(t => t.Name == "OverdueHeading"));
            Assert.AreEqual(Visibility.Collapsed, HeadingVisibility<CollapsibleHeading>(view));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_Completed_Smart_View_shows_its_Tasks_with_no_group_headings()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent", completed: true);
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.ShowSmartViewAsync(SmartView.Completed, Ct);
            var list = await WaitForRowsAsync(window.View, "OpenTaskList", "Pay rent", "Book flights");

            // One headingless group: its heading takes no room, so the first row starts at the top.
            Assert.AreEqual("Tasks", GroupName(list, TaskGroupKind.Open));
            Assert.AreEqual(0, Top(ShownRows(list).First(), list), 0.5);
            Assert.IsFalse(VisualTree.Descendants(list).OfType<TextBlock>().Any(t => t.Name == "OverdueHeading" && t.ActualHeight > 0));
            Assert.AreEqual(Visibility.Collapsed, HeadingVisibility<CollapsibleHeading>(window.View));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task The_selection_follows_a_row_into_Completed_N_and_back()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Water plants");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            window.View.TaskList.AnimationsEnabled = () => false;
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            (await CompletedHeadingAsync(view)).IsExpanded = true;
            var list = await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Pay rent", "Water plants");
            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");
            var row = viewModel.Tasks.Single(t => t.Title == "Water plants");
            list.SelectedItem = row;
            db.Clock.Advance(TimeSpan.FromMinutes(1)); // completed after "Book flights", so listed before it

            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));

            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Water plants", "Book flights");
            await UiWait.UntilAsync(() => ReferenceEquals(list.SelectedItem, row),
                () => $"The list selected '{(list.SelectedItem as TaskRowViewModel)?.Title}' after the row moved.", Ct);
            Assert.AreSame(row, viewModel.SelectedTask);

            Assert.IsTrue(view.TaskList.TryShortcut(VirtualKey.Space, VirtualKeyModifiers.None));

            await WaitForGroupRowsAsync(view, TaskGroupKind.Open, "Pay rent", "Water plants");
            await UiWait.UntilAsync(() => ReferenceEquals(list.SelectedItem, row),
                () => $"The list selected '{(list.SelectedItem as TaskRowViewModel)?.Title}' after the row moved back.", Ct);
            Assert.AreSame(row, viewModel.SelectedTask);
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task Completed_N_keeps_its_expanded_state_from_page_to_page()
    {
        using var db = await DatabaseAsync();
        await AddAsync(db, "Pay rent");
        await AddAsync(db, "Book flights", completed: true);
        var (window, viewModel) = Open(db);
        try
        {
            await viewModel.LoadAsync(Ct);
            var view = window.View;
            var heading = await CompletedHeadingAsync(view);
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(heading)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");

            await viewModel.ShowSmartViewAsync(SmartView.All, Ct);
            await WaitForRowsAsync(view, "OpenTaskList", "Pay rent");
            await viewModel.ShowSmartViewAsync(null, Ct);

            await WaitForGroupRowsAsync(view, TaskGroupKind.Completed, "Book flights");
            Assert.IsTrue((await CompletedHeadingAsync(view)).IsExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    private static double Top(FrameworkElement element, UIElement list) =>
        element.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;

    private static double Bottom(FrameworkElement element, UIElement list) => Top(element, list) + element.ActualHeight;

    private static void Toggle(CheckBox check) =>
        ((ToggleButtonAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(check)).Toggle();

    // The rows the list shows; containers it keeps for recycling stay in the visual tree but hold no item.
    private static List<string> RowNames(ListView list) =>
        [.. VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer)
            .Select(AutomationProperties.GetName)];

    // The shown row containers of the list, in order; containers kept for recycling hold no item.
    private static IEnumerable<ListViewItem> ShownRows(ListView list) =>
        VisualTree.Descendants(list).OfType<ListViewItem>()
            .Where(c => list.IndexFromContainer(c) >= 0)
            .OrderBy(list.IndexFromContainer);

    // The group a row container sits in, read from its heading's container.
    private static TaskGroupKind? GroupOf(ListView list, ListViewItem row) =>
        (list.GroupHeaderContainerFromItemContainer(row) as ListViewHeaderItem)?.Content is TaskGroup group
            ? group.Kind
            : null;

    private static List<string> GroupRowNames(ListView list, TaskGroupKind kind) =>
        [.. ShownRows(list).Where(c => GroupOf(list, c) == kind).Select(AutomationProperties.GetName)];

    private static TaskRowViewModel Row(ListView list, string title) =>
        ShownRows(list).Select(c => (TaskRowViewModel)c.Content).Single(r => r.Title == title);

    // The accessible name UI Automation gives a group of the list.
    private static string GroupName(ListView list, TaskGroupKind kind) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(VisualTree.Descendants(list).OfType<ListViewHeaderItem>()
            .Single(h => h.Content is TaskGroup group && group.Kind == kind)).GetName();

    // Visible while a heading of that type (and name) shows in the list; a group with no rows has no heading.
    private static Visibility HeadingVisibility<T>(MainView view, string? name = null) where T : FrameworkElement =>
        VisualTree.Descendants(VisualTree.Find<ListView>(view, "OpenTaskList")).OfType<T>()
            .Any(h => (name is null || h.Name == name) && h.Visibility == Visibility.Visible && h.ActualHeight > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Waits for the list's "Completed (N)" heading and returns it.
    private async Task<CollapsibleHeading> CompletedHeadingAsync(MainView view)
    {
        CollapsibleHeading? Shown() => VisualTree.Descendants(VisualTree.Find<ListView>(view, "OpenTaskList"))
            .OfType<CollapsibleHeading>().SingleOrDefault(h => h.ActualHeight > 0);
        await UiWait.UntilAsync(() => Shown() is not null, () => "No Completed heading is shown.", Ct);
        return Shown()!;
    }

    // Waits until one group of the Task list shows exactly these rows (by their accessible names), and returns
    // the list.
    private async Task<ListView> WaitForGroupRowsAsync(MainView view, TaskGroupKind kind, params string[] titles)
    {
        var list = VisualTree.Find<ListView>(view, "OpenTaskList");
        await UiWait.UntilAsync(() => GroupRowNames(list, kind).SequenceEqual(titles),
            () => $"{kind}: expected [{string.Join(", ", titles)}], was [{string.Join(", ", GroupRowNames(list, kind))}].",
            Ct);
        return list;
    }

    // Waits until the named Task list shows exactly these rows (by their accessible names), and returns it.
    private async Task<ListView> WaitForRowsAsync(MainView view, string name, params string[] titles)
    {
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
