using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class HotkeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taskdesk-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    [InlineData("Shift+Win+Q", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'Q')]
    [InlineData("Ctrl+F12", HotkeyModifiers.Ctrl, 0x7B)]
    [InlineData("Alt+7", HotkeyModifiers.Alt, '7')]
    public void Parses_and_formats_round_trip(string text, HotkeyModifiers modifiers, int vk)
    {
        var hotkey = Hotkey.TryParse(text);

        Assert.Equal(new Hotkey(modifiers, vk), hotkey);
        Assert.Equal(text, hotkey!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+Alt")]
    public void Rejects_text_that_is_not_a_shortcut(string text) => Assert.Null(Hotkey.TryParse(text));

    [Fact]
    public void A_shortcut_without_a_modifier_is_invalid() =>
        Assert.False(new Hotkey(HotkeyModifiers.None, 'A').IsValid);

    [Fact]
    public void Default_is_ctrl_alt_space() => Assert.Equal("Ctrl+Alt+Space", Hotkey.Default.ToString());

    [Fact]
    public void Settings_default_to_the_default_hotkey()
    {
        Assert.Equal(Hotkey.Default, new JsonShellSettingsStore(FilePath).Load().Hotkey);
    }

    [Fact]
    public void A_custom_hotkey_round_trips()
    {
        var store = new JsonShellSettingsStore(FilePath);
        var custom = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'K');
        store.Save(new ShellSettings { Hotkey = custom });

        Assert.Equal(custom, new JsonShellSettingsStore(FilePath).Load().Hotkey);
    }

    [Fact]
    public void A_disabled_hotkey_round_trips_as_disabled()
    {
        var store = new JsonShellSettingsStore(FilePath);
        store.Save(new ShellSettings { Hotkey = null });

        Assert.Null(new JsonShellSettingsStore(FilePath).Load().Hotkey);
    }

    [Fact]
    public void An_unreadable_stored_hotkey_falls_back_to_the_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"globalHotkey":"Ctrl+Nonsense"}""");

        Assert.Equal(Hotkey.Default, new JsonShellSettingsStore(FilePath).Load().Hotkey);
    }

    [Fact]
    public void Saving_other_settings_with_a_loaded_copy_keeps_the_hotkey()
    {
        var store = new JsonShellSettingsStore(FilePath);
        var custom = new Hotkey(HotkeyModifiers.Alt, 'J');
        store.Save(new ShellSettings { Hotkey = custom });

        store.Save(store.Load() with { Theme = ThemeChoice.Dark });

        var loaded = new JsonShellSettingsStore(FilePath).Load();
        Assert.Equal(custom, loaded.Hotkey);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
    }
}
