using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class SettingsPersistenceTests : IDisposable
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

    [Fact]
    public void Defaults_are_system_theme_hide_to_tray_and_nine_am()
    {
        var settings = new JsonShellSettingsStore(FilePath).Load();

        Assert.Equal(ThemeChoice.System, settings.Theme);
        Assert.Equal(CloseBehavior.HideToTray, settings.CloseBehavior);
        Assert.Equal(new TimeOnly(9, 0), settings.DefaultReminderTime);
        Assert.Null(settings.Layout);
    }

    [Fact]
    public void Preferences_round_trip()
    {
        var store = new JsonShellSettingsStore(FilePath);
        store.Save(new ShellSettings(
            Theme: ThemeChoice.Dark,
            CloseBehavior: CloseBehavior.Exit,
            DefaultReminderTime: new TimeOnly(7, 30)));

        var loaded = new JsonShellSettingsStore(FilePath).Load();

        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal(CloseBehavior.Exit, loaded.CloseBehavior);
        Assert.Equal(new TimeOnly(7, 30), loaded.DefaultReminderTime);
    }

    [Fact]
    public void Window_layout_with_pane_widths_round_trips()
    {
        var layout = new WindowLayout(10, 20, 1000, 700, true, new Dictionary<string, double> { ["nav"] = 220, ["detail"] = 320 });
        new JsonShellSettingsStore(FilePath).Save(new ShellSettings(Layout: layout));

        var loaded = new JsonShellSettingsStore(FilePath).Load().Layout;

        Assert.NotNull(loaded);
        Assert.Equal((10d, 20d, 1000d, 700d, true), (loaded.Left, loaded.Top, loaded.Width, loaded.Height, loaded.Maximized));
        Assert.Equal(220, loaded.PaneWidths!["nav"]);
        Assert.Equal(320, loaded.PaneWidths["detail"]);
    }

    [Theory]
    [InlineData("{\"themeMode\":\"Neon\",\"closeBehavior\":5,\"defaultReminderTime\":\"25:99\",\"windowLayout\":{\"left\":\"x\"}}")]
    [InlineData("{\"themeMode\":null,\"closeBehavior\":\"\",\"defaultReminderTime\":12,\"windowLayout\":3}")]
    public void Invalid_values_fall_back_to_defaults(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);

        Assert.Equal(new ShellSettings(), new JsonShellSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Save_keeps_unknown_keys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"hotkey\":\"CtrlAltSpace\"}");

        new JsonShellSettingsStore(FilePath).Save(new ShellSettings(Theme: ThemeChoice.Light));

        Assert.Contains("\"hotkey\":\"CtrlAltSpace\"", File.ReadAllText(FilePath));
    }
}
