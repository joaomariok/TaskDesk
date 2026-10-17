using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>What must survive closing and starting the app again (MANUAL-TESTS: Tray and window; Lists, Tags and Tasks).</summary>
public sealed class RestartTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Detail_pane_edits_survive_a_restart()
    {
        _app.TypeInto(Ids.QuickAddBox, "Draft", pressEnter: true);
        OpenDetails("Draft");
        _app.TypeInto(Ids.DetailNotesBox, "Call the venue");
        _app.Focus(Ids.DetailTitleBox);
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(" v2");

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Draft v2");
        OpenDetails("Draft v2");
        Assert.Equal("Call the venue", _app.TextBox(Ids.DetailNotesBox).Text);
    }

    [Fact]
    public void Pane_widths_set_with_the_splitters_survive_a_restart()
    {
        // The detail pane is docked, with its splitter, only above 1100 px; the window size is restored on restart.
        _app.ResizeTo(1200, 600);
        _app.TypeInto(Ids.QuickAddBox, "Plan trip", pressEnter: true);
        OpenDetails("Plan trip");
        var navigationBefore = Width(Ids.NavigationPane);
        var detailBefore = Width(Ids.DetailPane);

        AppSession.DragBy(_app.Find(Ids.SidebarSplitter), 80);
        AppSession.DragBy(_app.Find(Ids.DetailSplitter), -60);
        AppSession.WaitUntil(() => Width(Ids.NavigationPane) > navigationBefore + 40 && Width(Ids.DetailPane) > detailBefore + 30,
            () => $"Dragging did not resize the panes: navigation {navigationBefore} -> {Width(Ids.NavigationPane)}, " +
                  $"detail {detailBefore} -> {Width(Ids.DetailPane)}.");
        var navigation = Width(Ids.NavigationPane);
        var detail = Width(Ids.DetailPane);

        _app.Restart();

        OpenDetails("Plan trip");
        Assert.InRange(Width(Ids.NavigationPane), navigation - 2, navigation + 2);
        Assert.InRange(Width(Ids.DetailPane), detail - 2, detail + 2);
    }

    private void OpenDetails(string title)
    {
        _app.SelectRow(Ids.OpenTaskList, title);
        Keyboard.Type(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == title, () => $"Details of '{title}' did not open.");
    }

    private int Width(string automationId) => _app.Find(automationId).BoundingRectangle.Width;
}
