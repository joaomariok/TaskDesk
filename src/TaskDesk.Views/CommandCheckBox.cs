using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace TaskDesk.Views;

/// <summary>
/// A check box whose Command does the work and whose <c>IsChecked</c> only shows the bound state: a click, Space
/// on the box and a screen reader's Toggle all run the Command once without ticking the box themselves. The
/// bound state then shows the result. It draws Fluent's CheckBox style.
/// </summary>
public sealed partial class CommandCheckBox : CheckBox
{
    public CommandCheckBox()
    {
        DefaultStyleKey = typeof(CheckBox);
    }

    // Fluent's template draws the box with square-ish corners whatever CornerRadius says; follow CornerRadius,
    // so a CornerRadius of half the box (the Task checkbox) draws it round.
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("NormalRectangle") is Rectangle box)
        {
            box.RadiusX = CornerRadius.TopLeft;
            box.RadiusY = CornerRadius.TopLeft;
        }
    }

    // ToggleButton.OnClick toggles, then runs the Command: leave the toggling to the bound state.
    protected override void OnToggle()
    {
    }
}
