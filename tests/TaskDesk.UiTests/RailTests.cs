using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>The sidebar below 900 px: an icon rail with a Tags flyout and the full sidebar as an overlay.</summary>
public sealed class RailTests : IDisposable
{
    private const double NarrowFactor = 0.94; // 900 px -> about 846 px

    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    private static IReadOnlyList<string> Names(AutomationElement list) =>
        list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Select(i => i.Name).ToList();

    [Fact]
    public void Below_900_px_the_sidebar_is_a_rail_whose_items_keep_their_names_and_it_is_full_again_when_widened()
    {
        var listNames = _app.Rows(Ids.ListsList);
        Assert.NotEmpty(listNames);
        Assert.NotNull(_app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SmartViewList)));
        Assert.Null(_app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SidebarExpandButton)));

        _app.ResizeWidth(NarrowFactor);

        var smartViews = AppSession.Eventually(
            () => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.RailSmartViewList)),
            "The sidebar did not collapse to the rail below 900 px.");
        Assert.Equal(["Today", "Upcoming", "Overdue", "All", "Completed"], Names(smartViews));
        Assert.Equal(listNames, _app.Rows(Ids.RailListsList));
        Assert.Equal("Show sidebar", _app.Find(Ids.SidebarExpandButton).Name);
        AppSession.WaitUntil(() => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SmartViewList)) is null,
            () => "The full sidebar is still shown beside the rail.");

        _app.ResizeWidth(1 / NarrowFactor);

        AppSession.Eventually(
            () => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.SmartViewList)),
            "Widening past 900 px did not restore the full sidebar.");
        AppSession.WaitUntil(() => _app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.RailSmartViewList)) is null,
            () => "The rail is still shown beside the full sidebar.");
    }

    [Fact]
    public void The_hamburger_opens_the_full_sidebar_as_an_overlay_that_Esc_and_picking_an_item_close()
    {
        _app.ResizeWidth(NarrowFactor);

        _app.Find(Ids.SidebarExpandButton).AsButton().Invoke();
        AppSession.Eventually(() => _app.FindOnDesktop(Ids.SmartViewList), "The hamburger opened no sidebar overlay.");
        AppSession.WaitUntil(() => _app.FocusedAutomationId() is { } id && id.StartsWith("SmartView", StringComparison.Ordinal),
            () => $"Opening the overlay left focus on '{_app.FocusedAutomationId()}'.");

        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => _app.FindOnDesktop(Ids.SmartViewList) is null, () => "Esc did not close the overlay.");
        AppSession.WaitUntil(() => _app.FocusedAutomationId() == Ids.SidebarExpandButton,
            () => $"Closing the overlay left focus on '{_app.FocusedAutomationId()}'.");

        _app.Find(Ids.SidebarExpandButton).AsButton().Invoke();
        var smartViews = AppSession.Eventually(() => _app.FindOnDesktop(Ids.SmartViewList), "The overlay did not reopen.");
        AppSession.Eventually(
                () => smartViews.FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName("All"))),
                "The overlay has no 'All' Smart View.")
            .AsListBoxItem().Select();
        AppSession.WaitUntil(() => _app.FindOnDesktop(Ids.SmartViewList) is null, () => "Picking an item did not close the overlay.");
    }

    [Fact]
    public void The_hash_button_opens_a_Tags_flyout_that_Esc_closes()
    {
        _app.TypeInto(Ids.QuickAddBox, "Pay rent", pressEnter: true);
        _app.SelectRow(Ids.OpenTaskList, "Pay rent");
        AppSession.Press(VirtualKeyShort.RETURN); // Enter opens the detail pane, which holds the Tags
        _app.TypeInto(Ids.NewTagBox, "home", pressEnter: true);
        AppSession.WaitUntil(() => _app.Rows(Ids.TagList).Contains("home"),
            () => $"Tags were [{string.Join(", ", _app.Rows(Ids.TagList))}].");

        _app.ResizeWidth(NarrowFactor);

        _app.Find(Ids.RailTagsButton).AsButton().Invoke();
        var tags = AppSession.Eventually(() => _app.FindOnDesktop(Ids.RailTagList), "The # button opened no Tags flyout.");
        Assert.Contains("home", Names(tags));

        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => _app.FindOnDesktop(Ids.RailTagList) is null, () => "Esc did not close the Tags flyout.");
    }
}
