using Microsoft.UI.Xaml.Controls.Primitives;

namespace TaskDesk.Views;

/// <summary>
/// A toggle button whose Command does the work and whose <c>IsChecked</c> only shows the bound state: a click,
/// Space and a screen reader's Toggle all run the Command once without pressing the button themselves. The
/// bound state then shows the result. It draws Fluent's ToggleButton style.
/// </summary>
public sealed partial class CommandToggleButton : ToggleButton
{
    public CommandToggleButton()
    {
        DefaultStyleKey = typeof(ToggleButton);
    }

    // ToggleButton.OnClick toggles, then runs the Command: leave the toggling to the bound state.
    protected override void OnToggle()
    {
    }
}
