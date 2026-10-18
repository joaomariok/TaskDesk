using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

/// <summary>The Settings shortcut box reads a key press as a shortcut, as the WPF box did.</summary>
public sealed class HotkeyCaptureTests
{
    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt;

    [Theory]
    // Virtual key, modifiers held, expected shortcut text.
    [InlineData(0x20, CtrlAlt, "Ctrl+Alt+Space")]
    [InlineData(0x51, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Q")]
    [InlineData(0x7A, HotkeyModifiers.Win, "Win+F11")]
    // A key without a modifier is still captured; Settings then says it is not a valid shortcut.
    [InlineData(0x41, HotkeyModifiers.None, "A")]
    // Tab with a modifier is a shortcut, not focus movement.
    [InlineData(0x09, HotkeyModifiers.Ctrl, "Ctrl+Tab")]
    public void A_key_with_its_modifiers_is_captured_as_a_shortcut(int virtualKey, HotkeyModifiers modifiers, string expected)
    {
        var press = HotkeyCapture.Read(virtualKey, modifiers);

        Assert.True(press.Handled);
        Assert.Equal(expected, press.Hotkey?.ToString());
    }

    [Theory]
    // Shift, Ctrl, Alt (generic, left and right) and both Windows keys, held on their own.
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0xA0)]
    [InlineData(0xA1)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0x00)]
    public void A_modifier_key_alone_is_swallowed_without_a_shortcut(int virtualKey)
    {
        var press = HotkeyCapture.Read(virtualKey, HotkeyModifiers.Ctrl);

        Assert.True(press.Handled);
        Assert.Null(press.Hotkey);
    }

    [Fact]
    public void Tab_alone_is_left_to_move_the_focus()
    {
        var press = HotkeyCapture.Read(0x09, HotkeyModifiers.None);

        Assert.False(press.Handled);
        Assert.Null(press.Hotkey);
    }
}
