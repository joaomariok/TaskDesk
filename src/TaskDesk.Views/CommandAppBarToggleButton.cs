using Microsoft.UI.Xaml.Controls;

namespace TaskDesk.Views;

/// <summary>
/// A command bar's toggle button whose Command does the work and whose <c>IsChecked</c> only shows the bound
/// state, like <see cref="CommandToggleButton"/>: a click, Space and a screen reader's Toggle all run the Command
/// once without pressing the button themselves. It draws Fluent's AppBarToggleButton style (the implicit style in
/// Foundation.xaml).
/// </summary>
public sealed partial class CommandAppBarToggleButton : AppBarToggleButton
{
    public CommandAppBarToggleButton()
    {
        DefaultStyleKey = typeof(AppBarToggleButton);
    }

    // ToggleButton.OnClick toggles, then runs the Command: leave the toggling to the bound state.
    protected override void OnToggle()
    {
    }
}
