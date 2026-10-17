using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
        void Shot(int step, string state, Window? window = null)
        {
            if (!string.IsNullOrWhiteSpace(shots))
            {
                Directory.CreateDirectory(shots);
                var path = Path.Combine(shots, $"{theme.ToLowerInvariant()}-{step:00}-{state}.png");
                if (window is null)
                {
                    app.Capture(path);
                }
                else
                {
                    Thread.Sleep(300);
                    AppSession.CaptureFrame(window, path);
                }
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
        app.WaitForRows(Ids.OpenTaskList, "Water plants");
        Shot(5, "completed");

        // Ctrl+, opens Settings, the same window as the tray menu's Settings.
        AppSession.Ctrl(VirtualKeyShort.OEM_COMMA);
        var settings = app.SettingsWindow();
        AppSession.Eventually(() => settings.FindFirstDescendant(cf => cf.ByName("Close button behavior")),
            "Settings has no Close button behavior control.");
        Shot(9, "settings", settings);
        settings.Close();
        AppSession.WaitUntil(() => !app.SettingsWindowShown(), () => "The Settings window did not close.");

        // An Overdue Task: yesterday typed into the detail pane's Due date picker, which parses with the current culture.
        AppSession.Ctrl(VirtualKeyShort.KEY_4);
        app.TypeInto(Ids.QuickAddBox, "Pay the rent", pressEnter: true);
        app.SelectRow(Ids.OpenTaskList, "Pay the rent");
        Keyboard.Type(VirtualKeyShort.F2);
        AppSession.WaitUntil(() => app.TextBox(Ids.DetailTitleBox).Text == "Pay the rent",
            () => "The detail pane did not open.");
        var yesterday = DateTime.Today.AddDays(-1).ToString("d", CultureInfo.CurrentCulture);
        var dueBox = app.Find(Ids.DetailDueDatePicker).FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))
            ?? throw new InvalidOperationException("The Due date picker has no text box.");
        dueBox.Focus();
        Keyboard.Type(yesterday);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Keyboard.Type(VirtualKeyShort.TAB);
        // The Due edit saves after a short debounce, so reopen Overdue until the Task shows there.
        AppSession.WaitUntil(() =>
            {
                AppSession.Ctrl(VirtualKeyShort.KEY_3);
                Thread.Sleep(300);
                return app.Rows(Ids.OpenTaskList).Contains("Pay the rent");
            },
            () => $"Overdue rows were [{string.Join(", ", app.Rows(Ids.OpenTaskList))}]; the typed Due '{yesterday}' did not save.");
        AppSession.WaitUntil(() => app.Find(Ids.PageHeading).Name == "Overdue", () => "The Overdue view did not open.");
        // Close the detail pane so the row's red Due chip is in the shot.
        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.DetailTitleBox)) is null,
            () => "Esc did not close the detail pane.");
        Shot(10, "overdue");

        // The Filter row open with one Filter on, so "Clear filters" shows; then everything back off.
        AutomationElement? HasDue() => app.Window.FindFirstDescendant(cf => cf.ByName("Has due filter"));
        var filter = app.Find(Ids.FilterButton);
        filter.Click();
        AppSession.Eventually(HasDue, "Clicking Filter did not show the Filter row.");
        HasDue()!.Click();
        AppSession.WaitUntil(() => HasDue()!.AsToggleButton().ToggleState == ToggleState.On,
            () => "The Has due Filter did not turn on.");
        AppSession.Eventually(() => app.Window.FindFirstDescendant(cf => cf.ByName("Clear filters")),
            "Clear filters is not shown while a Filter is on.");
        Shot(11, "filters");
        HasDue()!.Click();
        AppSession.WaitUntil(() => HasDue()!.AsToggleButton().ToggleState == ToggleState.Off,
            () => "The Has due Filter did not turn off.");
        filter.Click();
        AppSession.WaitUntil(() => HasDue() is null, () => "Clicking Filter again did not hide the Filter row.");

        // Below 900 px the sidebar is the icon rail; the hamburger opens the full sidebar over it.
        app.ResizeWidth(0.94);
        AppSession.Eventually(() => app.Window.FindFirstDescendant(cf => cf.ByAutomationId(Ids.RailSmartViewList)),
            "The sidebar did not collapse to the rail.");
        Shot(7, "narrow");
        app.Find(Ids.SidebarExpandButton).AsButton().Invoke();
        AppSession.Eventually(() => app.FindOnDesktop(Ids.SmartViewList), "The hamburger opened no sidebar overlay.");
        Shot(8, "narrow-overlay");
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
