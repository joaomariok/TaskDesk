using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace TaskDesk.Views;

/// <summary>The modifier keys held down right now, for key handlers that pass them to a TryShortcut seam.</summary>
public static class KeyboardModifiers
{
    /// <summary>Ctrl, Shift and Alt as held now; with <paramref name="withWindows"/>, either Windows key too.</summary>
    public static VirtualKeyModifiers Current(bool withWindows = false)
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        if (withWindows && (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)))
        {
            modifiers |= VirtualKeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
