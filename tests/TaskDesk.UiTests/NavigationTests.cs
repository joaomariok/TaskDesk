using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>Smart Views, search, the detail pane, Lists and Tags (MANUAL-TESTS: Keyboard-only; Lists, Tags and Tasks).</summary>
public sealed class NavigationTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Ctrl_1_to_Ctrl_5_switch_Smart_Views()
    {
        // Quick add in Today makes the Task due today; in All it has no Due.
        AppSession.Ctrl(VirtualKeyShort.KEY_1);
        // Switching views is asynchronous; quick add only targets Today once the page shows it.
        AppSession.WaitUntil(() => _app.Find(Ids.PageHeading).Name == "Today",
            () => $"The page heading was '{_app.Find(Ids.PageHeading).Name}'.");
        _app.TypeInto(Ids.QuickAddBox, "Due today", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Due today");
        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        _app.TypeInto(Ids.QuickAddBox, "No due", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Due today", "No due");
        _app.SelectRow(Ids.OpenTaskList, "No due");
        AppSession.Press(VirtualKeyShort.SPACE);
        _app.WaitForRows(Ids.OpenTaskList, "Due today");

        AppSession.Ctrl(VirtualKeyShort.KEY_5);
        _app.WaitForRows(Ids.OpenTaskList);
        _app.WaitForRows(Ids.CompletedTaskList, "No due");
        AppSession.Ctrl(VirtualKeyShort.KEY_3);
        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.Ctrl(VirtualKeyShort.KEY_1);
        _app.WaitForRows(Ids.OpenTaskList, "Due today");
        AppSession.Ctrl(VirtualKeyShort.KEY_2);
        _app.WaitForRows(Ids.OpenTaskList);
        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        _app.WaitForRows(Ids.OpenTaskList, "Due today");
    }

    [Fact]
    public void Ctrl_N_moves_focus_from_the_search_box_to_quick_add()
    {
        AppSession.Ctrl(VirtualKeyShort.KEY_F);
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.SearchBox,
            () => $"Ctrl+F left focus on '{_app.FocusedAutomationId()}'.");

        AppSession.Ctrl(VirtualKeyShort.KEY_N);
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.QuickAddBox,
            () => $"Ctrl+N left focus on '{_app.FocusedAutomationId()}'.");
    }

    [Fact]
    public void Clicking_Filter_shows_the_priority_Filter_and_clicking_again_hides_it()
    {
        AutomationElement? PriorityFilter() =>
            _app.Window.FindFirstDescendant(cf => cf.ByName("Priority filter"));
        var filter = _app.Find(Ids.FilterButton);
        Assert.Null(_app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.FilterBar)));
        Assert.Null(PriorityFilter());

        filter.Click();
        AppSession.Eventually(PriorityFilter, "Clicking Filter did not show the priority Filter.");
        AppSession.WaitUntil(() => filter.AsToggleButton().ToggleState == ToggleState.On,
            () => "The Filter button is not shown pressed while the Filter row is shown.");

        filter.Click();
        AppSession.WaitUntil(() => PriorityFilter() is null,
            () => "Clicking Filter again did not hide the priority Filter.");
        AppSession.WaitUntil(() => filter.AsToggleButton().ToggleState == ToggleState.Off,
            () => "The Filter button still looks pressed after the Filter row was hidden.");
    }

    [Fact]
    public void Clicking_Filter_while_a_Filter_is_on_keeps_the_row_and_the_button_pressed()
    {
        AutomationElement? PriorityFilter() =>
            _app.Window.FindFirstDescendant(cf => cf.ByName("Priority filter"));
        AutomationElement? HasDue() =>
            _app.Window.FindFirstDescendant(cf => cf.ByName("Has due filter"));
        var filter = _app.Find(Ids.FilterButton);

        filter.Click();
        AppSession.Eventually(HasDue, "Clicking Filter did not show the Filter row.");
        HasDue()!.Click();
        AppSession.WaitUntil(() => HasDue()!.AsToggleButton().ToggleState == ToggleState.On,
            () => "The Has due Filter did not turn on.");
        try
        {
            filter.Click();

            AppSession.WaitUntil(() => filter.AsToggleButton().ToggleState == ToggleState.On,
                () => "The Filter button was released although a Filter is on.");
            Assert.NotNull(PriorityFilter());
        }
        finally
        {
            HasDue()?.Click();
            AppSession.WaitUntil(() => HasDue() is null || HasDue()!.AsToggleButton().ToggleState == ToggleState.Off,
                () => "The Has due Filter did not turn off.");
        }
    }

    [Fact]
    public void Ctrl_F_searches_and_Esc_clears_the_search()
    {
        _app.TypeInto(Ids.QuickAddBox, "Alpha report", pressEnter: true);
        _app.TypeInto(Ids.QuickAddBox, "Beta review", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Alpha report", "Beta review");

        AppSession.Ctrl(VirtualKeyShort.KEY_F);
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.SearchBox,
            () => $"Ctrl+F left focus on '{_app.FocusedAutomationId()}'.");
        Keyboard.Type("alpha");
        _app.WaitForRows(Ids.OpenTaskList, "Alpha report");

        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => _app.TextBox(Ids.SearchBox).Text.Length == 0, () => "Esc did not clear the search.");
        _app.WaitForRows(Ids.OpenTaskList, "Alpha report", "Beta review");
    }

    [Fact]
    public void F2_opens_the_detail_pane_on_the_selected_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Book dentist", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Book dentist");

        AppSession.Press(VirtualKeyShort.F2);

        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == "Book dentist",
            () => "The detail pane did not show the selected Task.");
    }

    [Fact]
    public void Enter_in_the_new_List_box_creates_a_List()
    {
        _app.Find(Ids.NewListButton).AsButton().Invoke();
        _app.TypeInto(Ids.NewListBox, "Groceries", pressEnter: true);

        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
    }

    [Fact]
    public void Enter_in_the_new_Tag_box_tags_the_selected_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Pay rent", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Pay rent");
        AppSession.Press(VirtualKeyShort.RETURN); // Enter opens the detail pane, which holds the Tags

        _app.TypeInto(Ids.NewTagBox, "home", pressEnter: true);

        AppSession.WaitUntil(() => _app.Rows(Ids.TagList).Contains("home"),
            () => $"Tags were [{string.Join(", ", _app.Rows(Ids.TagList))}].");
    }

    [Fact]
    public void Shift_F10_on_a_List_opens_its_context_menu_with_Rename_Colour_and_Delete()
    {
        _app.Find(Ids.NewListButton).AsButton().Invoke();
        _app.TypeInto(Ids.NewListBox, "Groceries", pressEnter: true);
        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
        _app.SelectRow(Ids.ListsList, "Groceries");

        AppSession.Press(VirtualKeyShort.SHIFT, VirtualKeyShort.F10);

        // A context menu is a pop-up window of its own, so look from the desktop.
        var desktop = _app.Window.Automation.GetDesktop();
        foreach (var entry in new[] { "Rename", "Colour", "Delete" })
        {
            AppSession.Eventually(
                () => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(entry))),
                $"The List's context menu has no '{entry}' entry.");
        }

        AppSession.Press(VirtualKeyShort.ESCAPE);
    }

    [Fact]
    public void An_empty_Task_list_takes_no_keyboard_focus_and_a_list_with_rows_does()
    {
        AppSession.Ctrl(VirtualKeyShort.KEY_5); // Completed: no open rows
        AppSession.WaitUntil(() => _app.Find(Ids.PageHeading).Name == "Completed",
            () => $"The page heading was '{_app.Find(Ids.PageHeading).Name}'.");
        AppSession.WaitUntil(() => !_app.Find(Ids.OpenTaskList).Properties.IsKeyboardFocusable.ValueOrDefault,
            () => "The empty open-Task list can take keyboard focus.");

        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        _app.TypeInto(Ids.QuickAddBox, "Pay rent", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Pay rent");
        _app.SelectRow(Ids.OpenTaskList, "Pay rent");
    }

    [Fact]
    public void Selecting_Overdue_in_the_sidebar_shows_the_Overdue_view()
    {
        _app.TypeInto(Ids.QuickAddBox, "No due yet", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "No due yet");

        _app.SelectRow(Ids.SmartViewList, "Overdue");

        _app.WaitForRows(Ids.OpenTaskList);
    }

    [Fact]
    public void Delete_on_a_List_asks_in_the_Fluent_dialog_where_Esc_and_Enter_keep_it_and_Delete_removes_it()
    {
        _app.Find(Ids.NewListButton).AsButton().Invoke();
        _app.TypeInto(Ids.NewListBox, "Groceries", pressEnter: true);
        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
        var desktop = _app.Window.Automation.GetDesktop();
        AutomationElement? Dialog() => desktop.FindFirstDescendant(cf => cf.ByAutomationId("FluentDialog"));

        _app.SelectRow(Ids.ListsList, "Groceries");
        AppSession.Press(VirtualKeyShort.DELETE);
        AppSession.Eventually(Dialog, "Delete on a List showed no Fluent dialog.");
        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => Dialog() is null, () => "Esc did not close the dialog.");
        Assert.Contains("Groceries", _app.Rows(Ids.ListsList));

        // Deleting is destructive, so Enter answers Cancel.
        _app.SelectRow(Ids.ListsList, "Groceries");
        AppSession.Press(VirtualKeyShort.DELETE);
        AppSession.Eventually(Dialog, "Delete on a List showed no Fluent dialog.");
        AppSession.Press(VirtualKeyShort.RETURN);
        AppSession.WaitUntil(() => Dialog() is null, () => "Enter did not close the dialog.");
        Assert.Contains("Groceries", _app.Rows(Ids.ListsList));

        _app.SelectRow(Ids.ListsList, "Groceries");
        AppSession.Press(VirtualKeyShort.DELETE);
        var dialog = AppSession.Eventually(Dialog, "Delete on a List showed no Fluent dialog.");
        AppSession.Eventually(
                () => dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Delete"))),
                "The dialog has no 'Delete' button.")
            .AsButton().Invoke();

        AppSession.WaitUntil(() => !_app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
    }
}
