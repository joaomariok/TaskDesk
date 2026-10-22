using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>Dragging a Task row with the real mouse reorders the List and the order survives a restart.</summary>
public sealed class ReorderTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Dragging_a_row_reorders_the_List_and_the_order_survives_a_restart()
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        // Drag "First" onto "Third" in small steps, as a person would.
        AppSession.DragSlowly(Centre(Row("First")), Centre(Row("Third")));

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
    }

    [Theory]
    [InlineData(true, "Second", "Third", "First")] // bottom half of "Third": after it
    [InlineData(false, "Second", "First", "Third")] // top half of "Third": before it
    public void Dropping_on_a_rows_half_lands_above_or_below_it_and_the_order_survives_a_restart(
        bool bottomHalf, params string[] expected)
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        var target = Row("Third").BoundingRectangle;
        var y = bottomHalf ? target.Bottom - (target.Height / 4) : target.Top + (target.Height / 4);
        AppSession.DragSlowly(Centre(Row("First")), new Point(target.Left + (target.Width / 2), y));

        _app.WaitForRows(Ids.OpenTaskList, expected);

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, expected);
    }

    [Fact]
    public void An_open_Task_dragged_over_another_Tasks_Subtask_rows_lands_after_that_block_with_its_own_Subtasks()
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        AddSubtasks("First", "Sub one");
        AddSubtasks("Second", "Pack", "Book");
        Open("First");
        Open("Second");
        _app.WaitForRows(
            Ids.OpenTaskList, "First", "Subtask Sub one", "Second", "Subtask Pack", "Subtask Book", "Third");

        // Drop the open "First" on the middle of Second's first Subtask row: inside Second's block.
        AppSession.DragSlowly(Centre(Row("First")), Centre(Row("Subtask Pack")));

        _app.WaitForRows(
            Ids.OpenTaskList, "Second", "Subtask Pack", "Subtask Book", "First", "Subtask Sub one", "Third");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Second", "First", "Third");
    }

    [Fact]
    public void Dragging_a_row_leaves_the_selected_row_selected()
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        _app.SelectRow(Ids.OpenTaskList, "Second");
        var target = Row("Third").BoundingRectangle;
        AppSession.DragSlowly(
            Centre(Row("First")), new Point(target.Left + (target.Width / 2), target.Bottom - (target.Height / 4)));

        _app.WaitForRows(Ids.OpenTaskList, "Second", "Third", "First");
        Assert.True(IsSelected("Second"), "The selected row stays selected.");
        Assert.False(IsSelected("First"), "The dragged row is not selected by the drag.");
    }

    [Fact]
    public void Releasing_a_drag_outside_the_list_moves_nothing()
    {
        string[] titles = ["First", "Second", "Third"];
        for (var i = 0; i < titles.Length; i++)
        {
            _app.TypeInto(Ids.QuickAddBox, titles[i], pressEnter: true);
            _app.WaitForRows(Ids.OpenTaskList, titles[..(i + 1)]);
        }

        // Over the sidebar, left of the list, level with "Third".
        var list = _app.Find(Ids.OpenTaskList).BoundingRectangle;
        var third = Row("Third").BoundingRectangle;
        AppSession.DragSlowly(Centre(Row("First")), new Point(list.Left - 60, third.Top + (third.Height / 2)));
        Thread.Sleep(500);

        Assert.Equal(["First", "Second", "Third"], _app.Rows(Ids.OpenTaskList));
    }

    private bool IsSelected(string title) =>
        _app.FindRow(Ids.OpenTaskList, title)?.AsListBoxItem().IsSelected == true;

    // Adds Subtasks to a Task in the detail pane; Esc closes it and gives the row back its focus.
    private void AddSubtasks(string task, params string[] subtasks)
    {
        _app.SelectRow(Ids.OpenTaskList, task);
        AppSession.Press(VirtualKeyShort.RETURN);
        foreach (var subtask in subtasks)
        {
            _app.TypeInto(Ids.NewSubtaskBox, subtask, pressEnter: true);
        }

        AppSession.WaitUntil(
            () => _app.TextBox(Ids.NewSubtaskBox).Text.Length == 0, () => $"The Subtasks of '{task}' were not added.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
    }

    // Opens a Task's Subtask rows with the Right key on its selected row.
    private void Open(string task)
    {
        _app.SelectRow(Ids.OpenTaskList, task);
        AppSession.Press(VirtualKeyShort.RIGHT);
        AppSession.WaitUntil(
            () => Row(task).Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
            () => $"'{task}' did not open.");
    }

    private AutomationElement Row(string title) =>
        AppSession.Eventually(
            () => _app.FindRow(Ids.OpenTaskList, title),
            $"No row '{title}'.");

    private static Point Centre(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }
}
