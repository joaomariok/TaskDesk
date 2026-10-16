using System.Windows;
using System.Windows.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;

namespace TaskDesk.App;

/// <summary>Enum lists for the Settings window's drop-downs.</summary>
public static class SettingsOptions
{
    public static IReadOnlyList<ThemeChoice> Themes => SettingsViewModel.Themes;
    public static IReadOnlyList<CloseBehavior> CloseBehaviors => SettingsViewModel.CloseBehaviors;
}

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var keyboardModifiers = Keyboard.Modifiers;

        // Let Tab leave the box, and ignore the modifier keys on their own.
        if (key == Key.Tab && keyboardModifiers == ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (keyboardModifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        ((SettingsViewModel)DataContext).TryCaptureHotkey(new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key)));
    }
}
