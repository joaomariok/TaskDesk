using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Lists in the sidebar, with real input in a real process: a List dragged to a new place keeps it after a
/// restart, in the full sidebar and in the rail, opens no page, and released outside the Lists moves nothing; and
/// Shift+F10 and the Menu key open a List's context menu.
/// </summary>
public sealed class ListsPaneTests : IDisposable
{
    private readonly AppSession _app = AppSession.Start();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void A_List_dragged_to_a_new_place_keeps_it_after_a_restart()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            AddList(name);
        }

        _app.WaitForRows(Ids.ListsList, "Inbox", "A", "B", "C");

        // The rows slide in after each reload; aim at where they come to rest.
        var dragged = AppSession.SettledBounds(() => Row("C"));
        var target = AppSession.SettledBounds(() => Row("A"));
        AppSession.DragSlowly(
            new Point(dragged.Left + dragged.Width / 2, dragged.Top + dragged.Height / 2),
            new Point(target.Left + target.Width / 2, target.Top + target.Height / 4));

        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");

        _app.Restart();

        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");
    }

    [Fact]
    public void A_List_dragged_in_the_rail_moves_there_and_the_page_shown_stays()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            AddList(name);
        }

        var heading = _app.Find(Ids.PageHeading).Name;
        _app.ResizeWidth(0.94); // below 900 px: the rail
        _app.WaitForRows(Ids.RailListsList, "Inbox", "A", "B", "C");

        var dragged = AppSession.SettledBounds(() => RowIn(Ids.RailListsList, "C"));
        var target = AppSession.SettledBounds(() => RowIn(Ids.RailListsList, "A"));
        AppSession.DragSlowly(
            new Point(dragged.Left + dragged.Width / 2, dragged.Top + dragged.Height / 2),
            new Point(target.Left + target.Width / 2, target.Top + target.Height / 4));

        _app.WaitForRows(Ids.RailListsList, "Inbox", "C", "A", "B");
        Assert.Equal(heading, _app.Find(Ids.PageHeading).Name);
    }

    [Fact]
    public void Dragging_a_List_opens_nothing_and_a_release_outside_the_Lists_moves_nothing()
    {
        foreach (var name in new[] { "A", "B", "C" })
        {
            AddList(name);
        }

        _app.WaitForRows(Ids.ListsList, "Inbox", "A", "B", "C");
        var heading = _app.Find(Ids.PageHeading).Name;

        // C dropped between Inbox and A: C's page does not open.
        var c = AppSession.SettledBounds(() => Row("C"));
        var a = AppSession.SettledBounds(() => Row("A"));
        AppSession.DragSlowly(
            new Point(c.Left + c.Width / 2, c.Top + c.Height / 2),
            new Point(a.Left + a.Width / 2, a.Top + a.Height / 4));
        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");
        Thread.Sleep(500);
        Assert.Equal(heading, _app.Find(Ids.PageHeading).Name);

        // B released over the page, right of the sidebar, level with A: nothing moves.
        var list = _app.Find(Ids.ListsList).BoundingRectangle;
        var b = AppSession.SettledBounds(() => Row("B"));
        a = AppSession.SettledBounds(() => Row("A"));
        AppSession.DragSlowly(
            new Point(b.Left + b.Width / 2, b.Top + b.Height / 2),
            new Point(list.Right + 120, a.Top + a.Height / 4));
        Thread.Sleep(500);

        Assert.Equal(["Inbox", "C", "A", "B"], _app.Rows(Ids.ListsList));
        Assert.Equal(heading, _app.Find(Ids.PageHeading).Name);
    }

    [Fact]
    public void Shift_F10_and_the_Menu_key_on_a_List_open_its_context_menu_with_Rename_Colour_and_Delete()
    {
        AddList("Groceries");

        foreach (var keys in new[] { new[] { VirtualKeyShort.SHIFT, VirtualKeyShort.F10 }, new[] { VirtualKeyShort.APPS } })
        {
            _app.SelectRow(Ids.ListsList, "Groceries");

            AppSession.Press(keys);

            var desktop = _app.Window.Automation.GetDesktop();
            foreach (var entry in new[] { "Rename", "Colour", "Delete" })
            {
                AppSession.Eventually(
                    () => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(entry))),
                    $"{string.Join("+", keys)} opened no context menu with '{entry}'.");
            }

            AppSession.Press(VirtualKeyShort.ESCAPE);
            AppSession.WaitUntil(
                () => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Rename"))) is null,
                () => "Esc did not close the context menu.");
        }
    }

    private void AddList(string name)
    {
        _app.Find(Ids.NewListButton).AsButton().Invoke();
        _app.TypeInto(Ids.NewListBox, name, pressEnter: true);
        AppSession.WaitUntil(() => _app.Rows(Ids.ListsList).Contains(name),
            () => $"Lists were [{string.Join(", ", _app.Rows(Ids.ListsList))}].");
    }

    private AutomationElement Row(string name) => RowIn(Ids.ListsList, name);

    private AutomationElement RowIn(string listId, string name) =>
        AppSession.Eventually(
            () => _app.Find(listId).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))),
            $"No List row '{name}' in {listId}.");
}
