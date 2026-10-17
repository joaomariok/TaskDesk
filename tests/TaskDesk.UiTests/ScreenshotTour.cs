using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>
/// Walks the main window through a few typical states in each theme, and shows the quick-add card, asserting each step. With
/// <c>TASKDESK_UI_SCREENSHOTS</c> set to a folder it also saves a PNG per state, for eyeballing
/// (see the ui-check skill); without it, the tour is a smoke test.
/// </summary>
public sealed class ScreenshotTour : IDisposable
{
    public const string ScreenshotsVariable = "TASKDESK_UI_SCREENSHOTS";

    // A field, not a using: disposed after the test, so a failure leaves a screenshot (AppSession.CaptureIfFailed).
    private AppSession? _app;

    public void Dispose() => _app?.Dispose();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Main_window_states(string theme)
    {
        var app = _app = AppSession.Start(theme);
        var shots = Environment.GetEnvironmentVariable(ScreenshotsVariable);
        void Shot(int step, string state)
        {
            if (!string.IsNullOrWhiteSpace(shots))
            {
                Directory.CreateDirectory(shots);
                app.Capture(Path.Combine(shots, $"{theme.ToLowerInvariant()}-{step:00}-{state}.png"));
            }
        }

        app.WaitForRows(Ids.OpenTaskList);
        Shot(1, "empty");

        AppSession.Ctrl(VirtualKeyShort.KEY_1);
        app.TypeInto(Ids.QuickAddBox, "Call the bank", pressEnter: true);
        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        app.TypeInto(Ids.QuickAddBox, "Water plants", pressEnter: true);
        app.TypeInto(Ids.QuickAddBox, "Book dentist", pressEnter: true);
        app.WaitForRows(Ids.OpenTaskList, "Call the bank", "Water plants", "Book dentist");
        Shot(2, "tasks");

        app.SelectRow(Ids.OpenTaskList, "Book dentist");
        Keyboard.Type(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => app.TextBox(Ids.DetailTitleBox).Text == "Book dentist",
            () => "The detail pane did not open.");
        app.TypeInto(Ids.DetailNotesBox, "Ask about Friday afternoon.");
        Shot(3, "details");

        app.TypeInto(Ids.NewTagBox, "health", pressEnter: true);
        app.Find(Ids.NewListButton).AsButton().Invoke();
        app.TypeInto(Ids.NewListBox, "Groceries", pressEnter: true);
        AppSession.WaitUntil(() => app.Rows(Ids.TagList).Contains("health") && app.Rows(Ids.ListsList).Contains("Groceries"),
            () => $"Tags were [{string.Join(", ", app.Rows(Ids.TagList))}], " +
                  $"Lists were [{string.Join(", ", app.Rows(Ids.ListsList))}].");
        Shot(4, "list-and-tag");

        app.SelectRow(Ids.OpenTaskList, "Water plants");
        AppSession.Press(VirtualKeyShort.SPACE);
        AppSession.Ctrl(VirtualKeyShort.KEY_5);
        app.WaitForRows(Ids.CompletedTaskList, "Water plants");
        Shot(5, "completed");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Quick_add_card(string theme)
    {
        // A hotkey nobody else uses, so a TaskDesk the owner has running cannot take it.
        var app = _app = AppSession.Start(theme, "Ctrl+Alt+Shift+F11");
        AppSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SHIFT, VirtualKeyShort.F11);

        var card = app.QuickAddWindow();
        AppSession.WaitUntil(() => card.FindFirstDescendant(cf => cf.ByName("Task title")) is { } box
                                   && box.Properties.HasKeyboardFocus.ValueOrDefault,
            () => "The quick-add title box did not take focus.");
        Assert.NotNull(card.FindFirstDescendant(cf => cf.ByName("List")));
        Assert.NotNull(card.FindFirstDescendant(cf => cf.ByName("Due date")));

        var shots = Environment.GetEnvironmentVariable(ScreenshotsVariable);
        if (!string.IsNullOrWhiteSpace(shots))
        {
            Directory.CreateDirectory(shots);
            Thread.Sleep(300);
            FlaUI.Core.Capturing.Capture.Element(card).ToFile(Path.Combine(shots, $"{theme.ToLowerInvariant()}-06-quick-add.png"));
        }

        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => !app.QuickAddWindowShown(), () => "Esc did not close the quick-add window.");
    }
}
