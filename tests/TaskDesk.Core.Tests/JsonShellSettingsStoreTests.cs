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
}
