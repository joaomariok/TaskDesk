using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.Views;

/// <summary>
/// A check box whose Command does the work and whose <c>IsChecked</c> only shows the bound state: a click, Space
/// on the box and a screen reader's Toggle all run the Command once without ticking the box themselves. The
/// bound state then shows the result. It draws Fluent's CheckBox style (the implicit style in Foundation.xaml).
/// </summary>
public sealed partial class CommandCheckBox : CheckBox
{
    public CommandCheckBox()
    {
        DefaultStyleKey = typeof(CheckBox);
    }

    // ToggleButton.OnClick toggles, then runs the Command: leave the toggling to the bound state.
    protected override void OnToggle()
    {
    }
}
