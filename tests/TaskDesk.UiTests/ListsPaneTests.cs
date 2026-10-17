using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// The Lists in the WinUI build's sidebar, with real input in a real process: a List dragged to a new place keeps
/// it after a restart, and Shift+F10 and the Menu key open a List's context menu. Written for the WinUI build
/// (ui-tests.ps1 -WinUI).
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
        DragSlowly(
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

    // A drag a person makes: press, move in small steps (drag and drop needs real pointer moves; the cursor jumps
    // of Mouse.Drag send none), pause over the target, release.
    private static void DragSlowly(Point from, Point to)
    {
        MoveTo(from);
        Thread.Sleep(100);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        const int Steps = 40;
        for (var i = 1; i <= Steps; i++)
        {
            MoveTo(new Point(from.X + (to.X - from.X) * i / Steps, from.Y + (to.Y - from.Y) * i / Steps));
            Thread.Sleep(15);
        }

        Thread.Sleep(400);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(200);
    }

    private static void MoveTo(Point point)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        var input = new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                X = (int)Math.Round((point.X - left) * 65535.0 / (width - 1)),
                Y = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1)),
                Flags = MouseEventFMove | MouseEventFAbsolute | MouseEventFVirtualDesk,
            },
        };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<Input>()));
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFAbsolute = 0x8000;
    private const uint MouseEventFVirtualDesk = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT: the type, then a union whose largest member (MOUSEINPUT on x64) is all a mouse move needs.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
