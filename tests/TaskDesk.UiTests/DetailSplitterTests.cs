using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The detail splitter with real mouse input in a real process: dragging it resizes the docked detail pane, and
/// the width survives a restart.
/// </summary>
public sealed class DetailSplitterTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void The_detail_pane_width_set_with_its_splitter_survives_a_restart()
    {
        // The pane docks, with its splitter, only above 1100 px; the window size is restored on restart.
        _app.ResizeTo(1200, 640);
        _app.TypeInto(Ids.QuickAddBox, "Plan trip", pressEnter: true);
        _app.WaitForRows(Ids.OpenTaskList, "Plan trip");
        OpenDetails("Plan trip");
        var before = Width(Ids.DetailPane);

        AppSession.DragBy(_app.Find(Ids.DetailSplitter), -60);
        AppSession.WaitUntil(() => Width(Ids.DetailPane) > before + 30,
            () => $"Dragging the splitter did not widen the detail pane: {before} -> {Width(Ids.DetailPane)}.");
        var width = Width(Ids.DetailPane);

        _app.Restart();

        _app.WaitForRows(Ids.OpenTaskList, "Plan trip");
        OpenDetails("Plan trip");
        AppSession.WaitUntil(() => Math.Abs(Width(Ids.DetailPane) - width) <= 2,
            () => $"After a restart the detail pane is {Width(Ids.DetailPane)} px wide, not {width}.");
    }

    private void OpenDetails(string title)
    {
        _app.SelectRow(Ids.OpenTaskList, title);
        AppSession.Press(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => _app.TextBox(Ids.DetailTitleBox).Text == title, () => $"Details of '{title}' did not open.");
    }

    private int Width(string automationId) => _app.Find(automationId).BoundingRectangle.Width;
}
