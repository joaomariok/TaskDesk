using FlaUI.Core.WindowsAPI;

namespace TaskDesk.UiTests;

/// <summary>The quick-add card, opened by the global hotkey over any app.</summary>
public sealed class QuickAddTests : IDisposable
{
    // A field, not a using: disposed after the test, so a failure leaves a screenshot (AppSession.CaptureIfFailed).
    private AppSession? _app;

    public void Dispose() => _app?.Dispose();

    [Fact]
    public void The_hotkey_opens_the_card_with_its_title_focused_and_its_List_and_Due_fields_and_Esc_closes_it()
    {
        // A hotkey nobody else uses, so a TaskDesk the owner has running cannot take it.
        var app = _app = AppSession.Start(globalHotkey: "Ctrl+Alt+Shift+F11");
        AppSession.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SHIFT, VirtualKeyShort.F11);

        var card = app.QuickAddWindow();
        AppSession.WaitUntil(() => card.FindFirstDescendant(cf => cf.ByName("Task title")) is { } box
                                   && box.Properties.HasKeyboardFocus.ValueOrDefault,
            () => "The quick-add title box did not take focus.");
        Assert.NotNull(card.FindFirstDescendant(cf => cf.ByName("List")));
        Assert.NotNull(card.FindFirstDescendant(cf => cf.ByName("Due date")));

        AppSession.Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitUntil(() => !app.QuickAddWindowShown(), () => "Esc did not close the quick-add window.");
    }
}
