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
        _app.TypeInto(Ids.NewListBox, "Groceries", pressEnter: true);

        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
    }

    [Fact]
    public void Enter_in_the_new_Tag_box_tags_the_selected_Task()
    {
        _app.TypeInto(Ids.QuickAddBox, "Pay rent", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Pay rent");

        _app.TypeInto(Ids.NewTagBox, "home", pressEnter: true);

        AppSession.WaitUntil(() => _app.Rows(Ids.TagList).Contains("home"),
            () => $"Tags were [{string.Join(", ", _app.Rows(Ids.TagList))}].");
    }
}
