using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Smart Views in the WinUI build's navigation pane, with real input in a real process: Ctrl+1 to Ctrl+5,
/// the compact rail below 900 px, and the sidebar splitter's width surviving a restart. Written for the WinUI
/// build (ui-tests.ps1 -WinUI) and held by the WPF build too, whose rail is a separate list (RailSmartViewList).
/// </summary>
public sealed class SmartViewPaneTests : IDisposable
{
    private static readonly string[] SmartViews = ["Today", "Upcoming", "Overdue", "All", "Completed"];

    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Ctrl_1_to_Ctrl_5_select_the_Smart_Views()
    {
        var keys = new[] { VirtualKeyShort.KEY_1, VirtualKeyShort.KEY_2, VirtualKeyShort.KEY_3, VirtualKeyShort.KEY_4, VirtualKeyShort.KEY_5 };
        foreach (var n in new[] { 2, 0, 4, 1, 3 })
        {
            AppSession.Ctrl(keys[n]);
            AppSession.WaitUntil(() => IsSelected(SmartViews[n]),
                () => $"Ctrl+{n + 1} did not select {SmartViews[n]}; selected: [{string.Join(", ", SmartViews.Where(IsSelected))}].");
        }
    }

    [Fact]
    public void Below_900_px_the_Smart_Views_are_a_rail_that_keeps_their_names_and_wider_the_full_sidebar_returns()
    {
        _app.ResizeTo(1200, 640);
        Assert.Equal(SmartViews, SmartViewNames());
        AppSession.WaitUntil(() => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SidebarExpandButton)) is null,
            () => "The full sidebar shows a hamburger.");
        var fullWidth = Width(Ids.NavigationPane);

        _app.ResizeTo(850, 640);

        _app.Find(Ids.SidebarExpandButton);
        AppSession.WaitUntil(() => Width(Ids.NavigationPane) < fullWidth / 2,
            () => $"The pane is {Width(Ids.NavigationPane)} px wide in the rail (full: {fullWidth}).");
        // The WinUI rail is the same list, compact; the WPF rail is a list of its own.
        Assert.Equal(SmartViews, SmartViewNames(AppSession.IsWinUI ? Ids.SmartViewList : Ids.RailSmartViewList));
        AppSession.WaitUntil(() => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SidebarSplitter)) is null,
            () => "The rail shows the sidebar splitter.");

        _app.ResizeTo(1200, 640);

        AppSession.WaitUntil(() => Math.Abs(Width(Ids.NavigationPane) - fullWidth) <= 2,
            () => $"Widened, the pane is {Width(Ids.NavigationPane)} px wide, not {fullWidth}.");
        AppSession.WaitUntil(() => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SidebarExpandButton)) is null,
            () => "The full sidebar still shows the hamburger.");
    }

    [Fact]
    public void The_sidebar_width_set_with_its_splitter_survives_a_restart()
    {
        _app.ResizeTo(1200, 640);
        var before = Width(Ids.NavigationPane);

        AppSession.DragBy(_app.Find(Ids.SidebarSplitter), 80);
        AppSession.WaitUntil(() => Width(Ids.NavigationPane) > before + 40,
            () => $"Dragging the splitter did not widen the sidebar: {before} -> {Width(Ids.NavigationPane)}.");
        var width = Width(Ids.NavigationPane);

        _app.Restart();

        AppSession.WaitUntil(() => Math.Abs(Width(Ids.NavigationPane) - width) <= 2,
            () => $"After a restart the sidebar is {Width(Ids.NavigationPane)} px wide, not {width}.");
    }

    private bool IsSelected(string smartView) =>
        _app.Find("SmartView" + smartView).Patterns.SelectionItem.Pattern.IsSelected.Value;

    private List<string> SmartViewNames(string listId = Ids.SmartViewList) =>
        [.. _app.Find(listId).FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Select(i => i.Name).Where(SmartViews.Contains)];

    private int Width(string automationId) => _app.Find(automationId).BoundingRectangle.Width;
}
