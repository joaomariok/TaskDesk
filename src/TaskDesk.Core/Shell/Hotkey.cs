using System.Globalization;

namespace TaskDesk.Core.Shell;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global keyboard shortcut: modifier keys plus one Windows virtual-key code.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VkSpace = 0x20;

    public static readonly Hotkey Default = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, VkSpace);

    private static readonly (string Name, int Vk)[] NamedKeys =
    [
        ("Space", 0x20), ("Enter", 0x0D), ("Tab", 0x09), ("Backspace", 0x08),
        ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
        ("Insert", 0x2D), ("Delete", 0x2E),
    ];

    /// <summary>True when the shortcut has at least one modifier and a supported non-modifier key.</summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && KeyName(VirtualKey) is not null;

    /// <summary>Text such as "Ctrl+Alt+Space"; also the form stored in settings.json.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(VirtualKey) ?? "0x" + VirtualKey.ToString("X2", CultureInfo.InvariantCulture));
        return string.Join('+', parts);
    }

    /// <summary>Parses the <see cref="ToString"/> form; null when it is not a valid shortcut.</summary>
    public static Hotkey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    return null;
            }
        }

        var vk = VirtualKeyOf(parts[^1]);
        if (vk is null)
        {
            return null;
        }

        var hotkey = new Hotkey(modifiers, vk.Value);
        return hotkey.IsValid ? hotkey : null;
    }

    private static string? KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)vk).ToString();
        }

        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (name, code) in NamedKeys)
        {
            if (code == vk)
            {
                return name;
            }
        }

        return null;
    }

    private static int? VirtualKeyOf(string name)
    {
        for (var vk = 0x08; vk <= 0x87; vk++)
        {
            if (string.Equals(KeyName(vk), name, StringComparison.OrdinalIgnoreCase))
            {
                return vk;
            }
        }

        return null;
    }
}

/// <summary>Registers one system-wide hotkey and reports presses. Implementations marshal to the UI thread.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Raised when the registered shortcut is pressed.</summary>
    event Action? Pressed;

    /// <summary>
    /// Registers <paramref name="hotkey"/>, replacing the current one. Returns false when another
    /// application owns the combination; the previously registered hotkey then stays active.
    /// </summary>
    bool TryRegister(Hotkey hotkey);

    /// <summary>Removes the current hotkey, if any.</summary>
    void Unregister();
}
