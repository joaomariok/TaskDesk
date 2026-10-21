using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class JsonShellSettingsStoreTests : IDisposable
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
    public void Missing_file_defaults_to_start_with_windows_on_and_hint_unseen()
    {
        var settings = new JsonShellSettingsStore(FilePath).Load();

        Assert.True(settings.StartWithWindows);
        Assert.False(settings.TrayHintShown);
    }

    [Fact]
    public void Saved_values_round_trip()
    {
        var store = new JsonShellSettingsStore(FilePath);
        store.Save(new ShellSettings(StartWithWindows: false, TrayHintShown: true));

        Assert.Equal(new ShellSettings(false, true), new JsonShellSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Save_keeps_keys_owned_by_other_features()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"theme\":\"Dark\"}");

        new JsonShellSettingsStore(FilePath).Save(new ShellSettings());

        Assert.Contains("\"theme\":\"Dark\"", File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{not json");

        Assert.Equal(new ShellSettings(), new JsonShellSettingsStore(FilePath).Load());
    }

    [Theory]
    [InlineData("{\"startWithWindows\":\"yes\",\"trayHintShown\":\"yes\"}")]
    [InlineData("{\"startWithWindows\":0,\"trayHintShown\":1}")]
    [InlineData("{\"startWithWindows\":null,\"trayHintShown\":{}}")]
    [InlineData("{\"startWithWindows\":[true],\"trayHintShown\":[false]}")]
    public void A_boolean_key_of_the_wrong_type_reads_as_its_default(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);

        var settings = new JsonShellSettingsStore(FilePath).Load();

        Assert.True(settings.StartWithWindows);
        Assert.False(settings.TrayHintShown);
    }

    [Fact]
    public void A_wrong_typed_boolean_key_does_not_hide_the_other_keys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"startWithWindows\":\"yes\",\"trayHintShown\":true,\"themeMode\":\"Dark\"}");

        var settings = new JsonShellSettingsStore(FilePath).Load();

        Assert.True(settings.TrayHintShown);
        Assert.Equal(ThemeChoice.Dark, settings.Theme);
    }

    [Fact]
    public void An_unreadable_file_throws_instead_of_reading_as_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{}");
        using var held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => new JsonShellSettingsStore(FilePath).Load());
    }
}
