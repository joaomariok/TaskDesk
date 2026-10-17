using System.Windows.Controls;

namespace TaskDesk.App.Controls;

/// <summary>
/// A check box whose Command does the work and whose <c>IsChecked</c> only shows the bound state, like
/// <see cref="CommandToggleButton"/>: a click runs the Command once, and a screen reader's toggle action runs
/// it too instead of only ticking the box. Takes the CheckBox style unless one is set.
/// </summary>
public sealed class CommandCheckBox : CheckBox
{
    private bool _clicking;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // The implicit style is looked up by the exact type, so there is none: use Fluent's CheckBox style.
        // Only when no style is set: a style set inside a template has lower precedence than a value set
        // here, so setting it unconditionally (as the constructor once did) would override the template's.
        if (Style is null)
        {
            SetResourceReference(StyleProperty, typeof(CheckBox));
        }
    }

    protected override void OnClick()
    {
        // ToggleButton.OnClick toggles, then ButtonBase.OnClick runs the Command: skip the toggle.
        _clicking = true;
        try
        {
            base.OnClick();
        }
        finally
        {
            _clicking = false;
        }
    }

    protected override void OnToggle()
    {
        if (!_clicking)
        {
            // Reached from UI Automation's Toggle pattern: do what a click would.
            CommandToggleButton.ExecuteCommand(this);
        }
    }
}
