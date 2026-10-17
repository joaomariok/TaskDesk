using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TaskDesk.App.Controls;

/// <summary>
/// A toggle button whose <see cref="ICommandSource.Command"/> does the work and whose
/// <see cref="ToggleButton.IsChecked"/> only shows the bound state: bind <c>IsChecked</c> one-way.
/// Looks exactly like Fluent's ToggleButton.
/// </summary>
/// <remarks>
/// A plain ToggleButton flips <c>IsChecked</c> itself on every toggle, and UI Automation's Toggle pattern
/// (a screen reader's toggle action) only flips it, never running the Command: the button looks on while
/// nothing happened. Here a toggle never flips <c>IsChecked</c>; a click runs the Command as any button
/// does, and an automation toggle runs it too.
/// </remarks>
public sealed class CommandToggleButton : ToggleButton
{
    private bool _clicking;

    public CommandToggleButton()
    {
        // The implicit style is looked up by the exact type; use Fluent's ToggleButton style.
        SetResourceReference(StyleProperty, typeof(ToggleButton));
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
        if (_clicking)
        {
            return;
        }

        // Reached from UI Automation's Toggle pattern: do what a click would.
        ExecuteCommand(this);
    }

    /// <summary>Runs a toggle's Command as a click would, if it can execute.</summary>
    internal static void ExecuteCommand(ToggleButton button)
    {
        var target = button.CommandTarget ?? button;
        switch (button.Command)
        {
            case RoutedCommand routed when routed.CanExecute(button.CommandParameter, target):
                routed.Execute(button.CommandParameter, target);
                break;
            case { } command and not RoutedCommand when command.CanExecute(button.CommandParameter):
                command.Execute(button.CommandParameter);
                break;
        }
    }
}
