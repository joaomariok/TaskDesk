namespace TaskDesk.Core.Shell;

/// <summary>A key press in the Settings shortcut box: whether the box takes it, and the shortcut it makes, if any.</summary>
public sealed record HotkeyPress(bool Handled, Hotkey? Hotkey);

/// <summary>Reads a key press in the Settings shortcut box as a global shortcut.</summary>
public static class HotkeyCapture
{
    private const int VkTab = 0x09;

    // None, Shift, Ctrl, Alt, both Windows keys, and the left and right Shift, Ctrl and Alt.
    private static readonly int[] ModifierKeys = [0x00, 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    /// <summary>
    /// Tab alone is left to move the focus; a modifier key alone is taken without a shortcut, so the box waits for
    /// the rest of the combination; any other key makes a shortcut with the modifiers held. The shortcut may still
    /// be invalid (no modifier, unsupported key): Settings says so.
    /// </summary>
    public static HotkeyPress Read(int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey == VkTab && modifiers == HotkeyModifiers.None)
        {
            return new HotkeyPress(false, null);
        }

        return Array.IndexOf(ModifierKeys, virtualKey) >= 0
            ? new HotkeyPress(true, null)
            : new HotkeyPress(true, new Hotkey(modifiers, virtualKey));
    }
}
