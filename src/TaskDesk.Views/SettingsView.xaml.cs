using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.System;
using Windows.UI.Core;

namespace TaskDesk.Views;

/// <summary>The Settings window's content: the setting cards, and the global shortcut captured by pressing it.</summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    public IReadOnlyList<ThemeChoice> Themes => SettingsViewModel.Themes;

    public IReadOnlyList<CloseBehavior> CloseBehaviors => SettingsViewModel.CloseBehaviors;

    /// <summary>
    /// A key pressed in the shortcut box: tries the combination as the global shortcut (Settings shows why when it
    /// cannot be used). Returns false for Tab alone, which moves the focus on.
    /// </summary>
    public bool CaptureHotkey(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var press = HotkeyCapture.Read((int)key, ToHotkeyModifiers(modifiers));
        if (press.Hotkey is { } hotkey)
        {
            ViewModel.TryCaptureHotkey(hotkey);
        }

        return press.Handled;
    }

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    public static Visibility ShownWhenText(string? text) => HasText(text) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShownWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string VersionText(string version) => string.Format(CultureInfo.InvariantCulture, "Version {0}", version);

    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = CaptureHotkey(e.Key, CurrentModifiers());

    private static VirtualKeyModifiers CurrentModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= VirtualKeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static HotkeyModifiers ToHotkeyModifiers(VirtualKeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if (modifiers.HasFlag(VirtualKeyModifiers.Menu))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(VirtualKeyModifiers.Windows))
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }
}
