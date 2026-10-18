using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Lists in the sidebar, with real input in a real process: a List dragged to a new place keeps it after a
/// restart, and Shift+F10 and the Menu key open a List's context menu.
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

        var dragged = Row("C").BoundingRectangle;
        var target = Row("A").BoundingRectangle;
        AppSession.DragSlowly(
            new Point(dragged.Left + dragged.Width / 2, dragged.Top + dragged.Height / 2),
            new Point(target.Left + target.Width / 2, target.Top + target.Height / 4));

        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");

        _app.Restart();

        _app.WaitForRows(Ids.ListsList, "Inbox", "C", "A", "B");
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

    private AutomationElement Row(string name) =>
        AppSession.Eventually(
            () => _app.Find(Ids.ListsList).FindFirstChild(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))),
            $"No List row '{name}'.");
}
