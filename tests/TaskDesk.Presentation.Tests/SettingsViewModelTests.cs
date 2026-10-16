using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class SettingsViewModelTests
{
    private sealed class FakeStore : IShellSettingsStore
    {
        public ShellSettings Current { get; set; } = new();
        public int Saves { get; private set; }
        public ShellSettings Load() => Current;
        public void Save(ShellSettings settings)
        {
            Current = settings;
            Saves++;
        }
    }

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetValue(string name) => Values.GetValueOrDefault(name);
        public void SetValue(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
    }

    private sealed class FakeTheme : IThemeService
    {
        public List<ThemeChoice> Applied { get; } = [];
        public void Apply(ThemeChoice theme) => Applied.Add(theme);
    }

    private sealed class FakeOpener : IFolderOpener
    {
        public List<string> Opened { get; } = [];
        public void Open(string path) => Opened.Add(path);
    }

    private sealed class FakeBackups(Exception? failure = null) : TaskDesk.Core.Backups.IBackupService
    {
        public int Calls { get; private set; }

        public Task<string> BackUpNowAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return failure is null ? Task.FromResult(@"C:\b\x.db") : Task.FromException<string>(failure);
        }

        public Task<string?> BackUpIfDueAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Back_up_now_runs_a_backup_and_reports_the_file()
    {
        var backups = new FakeBackups();
        var vm = new SettingsViewModel(
            _store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3", backups);

        await vm.BackUpNowCommand.ExecuteAsync(null);

        Assert.Equal(1, backups.Calls);
        Assert.Contains(@"C:\b\x.db", vm.BackupStatus);
    }

    [Fact]
    public async Task A_failed_backup_is_reported_not_thrown()
    {
        var vm = new SettingsViewModel(
            _store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3",
            new FakeBackups(new IOException("disk full")));

        await vm.BackUpNowCommand.ExecuteAsync(null);

        Assert.Contains("disk full", vm.BackupStatus);
    }

    private const string Exe =@"C:\TaskDesk\TaskDesk.exe";
    private readonly FakeStore _store = new();
    private readonly FakeRunKey _runKey = new();
    private readonly FakeTheme _theme = new();
    private readonly FakeOpener _opener = new();
    private readonly IAppPaths _paths = AppPaths.ForRoot(@"C:\Data\TaskDesk");

    private SettingsViewModel Create() =>
        new(_store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3");

    [Fact]
    public void Shows_the_stored_values_without_writing_anything()
    {
        _store.Current = new ShellSettings(false, true, ThemeChoice.Dark, CloseBehavior.Exit, new TimeOnly(8, 15));

        var vm = Create();

        Assert.Equal(ThemeChoice.Dark, vm.Theme);
        Assert.False(vm.StartWithWindows);
        Assert.Equal(CloseBehavior.Exit, vm.CloseBehavior);
        Assert.Equal("08:15", vm.DefaultReminderTimeText);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_theme.Applied);
        Assert.Empty(_runKey.Values);
    }

    [Fact]
    public void Changing_theme_applies_it_immediately_and_saves()
    {
        var vm = Create();

        vm.Theme = ThemeChoice.Light;

        Assert.Equal([ThemeChoice.Light], _theme.Applied);
        Assert.Equal(ThemeChoice.Light, _store.Current.Theme);
    }

    [Fact]
    public void Toggling_start_with_windows_updates_the_run_key_and_saves()
    {
        var vm = Create();

        vm.StartWithWindows = false;
        Assert.Empty(_runKey.Values);
        Assert.False(_store.Current.StartWithWindows);

        vm.StartWithWindows = true;
        Assert.Equal($"\"{Exe}\" --minimized", _runKey.Values[AutoStartService.ValueName]);
        Assert.True(_store.Current.StartWithWindows);
    }

    [Fact]
    public void Changing_close_behavior_saves_it()
    {
        Create().CloseBehavior = CloseBehavior.Exit;

        Assert.Equal(CloseBehavior.Exit, _store.Current.CloseBehavior);
    }

    [Fact]
    public void Valid_reminder_time_is_saved_and_invalid_is_not()
    {
        var vm = Create();

        vm.DefaultReminderTimeText = "7:05";
        Assert.Equal(new TimeOnly(7, 5), _store.Current.DefaultReminderTime);
        Assert.True(vm.IsReminderTimeValid);

        vm.DefaultReminderTimeText = "banana";
        Assert.False(vm.IsReminderTimeValid);
        Assert.Equal(new TimeOnly(7, 5), _store.Current.DefaultReminderTime);
    }

    [Fact]
    public void Saving_a_preference_keeps_other_settings_such_as_the_layout()
    {
        var layout = new WindowLayout(1, 2, 900, 600);
        _store.Current = new ShellSettings(Layout: layout, TrayHintShown: true);

        Create().Theme = ThemeChoice.Dark;

        Assert.Same(layout, _store.Current.Layout);
        Assert.True(_store.Current.TrayHintShown);
    }

    [Fact]
    public void About_info_and_open_data_folder()
    {
        var vm = Create();

        Assert.Equal("1.2.3", vm.Version);
        Assert.Equal(_paths.DataDirectory, vm.DataFolder);
        Assert.Equal(_paths.LogsDirectory, vm.LogFolder);

        vm.OpenDataFolderCommand.Execute(null);

        Assert.Equal([_paths.DataDirectory], _opener.Opened);
    }
}
