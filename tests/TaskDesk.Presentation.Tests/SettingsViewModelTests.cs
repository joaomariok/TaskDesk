using Microsoft.Extensions.Time.Testing;
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
            _store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3", Hotkeys, backups);

        await vm.BackUpNowCommand.ExecuteAsync(null);

        Assert.Equal(1, backups.Calls);
        Assert.Contains(@"C:\b\x.db", vm.BackupStatus);
    }

    [Fact]
    public async Task A_failed_backup_is_reported_not_thrown()
    {
        var vm = new SettingsViewModel(
            _store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3", Hotkeys,
            new FakeBackups(new IOException("disk full")));

        await vm.BackUpNowCommand.ExecuteAsync(null);

        Assert.Contains("disk full", vm.BackupStatus);
    }

    private const string Exe = @"C:\TaskDesk\TaskDesk.exe";
    private readonly FakeStore _store = new();
    private readonly FakeRunKey _runKey = new();
    private readonly FakeTheme _theme = new();
    private readonly FakeOpener _opener = new();
    private readonly IAppPaths _paths = AppPaths.ForRoot(@"C:\Data\TaskDesk");

    private readonly FakeRegistrar _registrar = new();
    private HotkeyService? _hotkeyService;

    private HotkeyService Hotkeys => _hotkeyService ??= new HotkeyService(_registrar, _store);

    private SettingsViewModel Create() =>
        new(_store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3", Hotkeys);

    [Fact]
    public void Shows_the_stored_values_without_writing_anything()
    {
        _store.Current = new ShellSettings(false, true, ThemeChoice.Dark, CloseBehavior.Exit, new TimeOnly(8, 15));

        var vm = Create();

        Assert.Equal(ThemeChoice.Dark, vm.Theme);
        Assert.False(vm.StartWithWindows);
        Assert.Equal(CloseBehavior.Exit, vm.CloseBehavior);
        Assert.Equal(new TimeSpan(8, 15, 0), vm.DefaultReminderTime);
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
    public void Picking_a_default_reminder_time_saves_it_at_once()
    {
        var vm = Create();

        vm.DefaultReminderTime = new TimeSpan(7, 5, 0);

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

    private static UpdateScheduler NewScheduler(FakeAppUpdates updates) =>
        new(updates, RuntimeMode.Normal, new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));

    private SettingsViewModel CreateWithUpdates(UpdateScheduler scheduler, Func<Task>? restartToUpdate = null) =>
        new(_store, new AutoStartService(_runKey), _theme, _opener, _paths, Exe, "1.2.3", Hotkeys,
            updates: scheduler, restartToUpdate: restartToUpdate);

    [Fact]
    public async Task Restart_to_update_is_hidden_with_nothing_pending_and_shown_once_an_update_is_pending()
    {
        var updates = new FakeAppUpdates { Available = "1.3.0" };
        using var scheduler = NewScheduler(updates);
        var vm = CreateWithUpdates(scheduler);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(vm.IsUpdatePending);
        Assert.Null(vm.PendingUpdateVersion);

        scheduler.Start();
        await scheduler.CurrentCheck;

        Assert.True(vm.IsUpdatePending);
        Assert.Equal("1.3.0", vm.PendingUpdateVersion);
        Assert.Contains(nameof(SettingsViewModel.IsUpdatePending), changed);
    }

    [Fact]
    public async Task An_update_already_pending_shows_restart_to_update_when_Settings_opens()
    {
        var updates = new FakeAppUpdates { Available = "1.3.0" };
        using var scheduler = NewScheduler(updates);
        scheduler.Start();
        await scheduler.CurrentCheck;

        var vm = CreateWithUpdates(scheduler);

        Assert.True(vm.IsUpdatePending);
        Assert.Equal("1.3.0", vm.PendingUpdateVersion);
    }

    [Fact]
    public async Task A_stopped_Settings_no_longer_follows_the_pending_update()
    {
        var updates = new FakeAppUpdates { Available = "1.3.0" };
        using var scheduler = NewScheduler(updates);
        var vm = CreateWithUpdates(scheduler);

        vm.Stop();
        scheduler.Start();
        await scheduler.CurrentCheck;

        Assert.False(vm.IsUpdatePending);
    }

    [Fact]
    public async Task Restart_to_update_runs_the_exit_path_before_asking_the_port_to_apply()
    {
        var updates = new FakeAppUpdates { Available = "1.3.0" };
        using var scheduler = NewScheduler(updates);
        var lifetime = new FakeLifetime();
        var shell = new ShellController(
            new FakeTray(), new FakeWindow(), new FakeSingleInstance(), _store, new AutoStartService(_runKey), lifetime,
            updates: scheduler);
        var restartsDuringExit = -1;
        Func<Task> noPart = () => Task.CompletedTask;
        await shell.LaunchAsync(
            new ShellLaunch
            {
                Minimized = true,
                ExePath = @"C:\TaskDesk\TaskDesk.exe",
                ExitParts = new ShellExitParts
                {
                    PrepareWindow = () =>
                    {
                        restartsDuringExit = updates.Restarts;
                        return Task.CompletedTask;
                    },
                    StopHost = noPart,
                    StopSystemHooks = noPart,
                    ReleaseHotkey = noPart,
                    DisposeHost = noPart,
                },
                StartSystemHooks = () => { },
                Running = () => { },
            },
            TestContext.Current.CancellationToken);
        var vm = CreateWithUpdates(scheduler, shell.RestartToUpdateAsync);
        scheduler.Start();
        await scheduler.CurrentCheck;

        await vm.RestartToUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, restartsDuringExit);
        Assert.Equal(1, updates.Restarts);
    }

    private static readonly Hotkey Custom = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'K');

    [Fact]
    public void Shows_the_stored_hotkey_or_disabled()
    {
        Assert.Equal("Ctrl+Alt+Space", Create().HotkeyText);

        _store.Current = _store.Current with { Hotkey = null };
        Assert.Equal("Disabled", Create().HotkeyText);
    }

    [Fact]
    public void Capturing_a_free_hotkey_registers_it_and_saves_it_without_clobbering_other_fields()
    {
        _store.Current = new ShellSettings(Theme: ThemeChoice.Dark);
        var vm = Create();

        var ok = vm.TryCaptureHotkey(Custom);

        Assert.True(ok);
        Assert.Equal(Custom, _registrar.Current);
        Assert.Equal(Custom, _store.Current.Hotkey);
        Assert.Equal(ThemeChoice.Dark, _store.Current.Theme);
        Assert.Equal("Ctrl+Shift+K", vm.HotkeyText);
        Assert.Null(vm.HotkeyError);
    }

    [Fact]
    public void Capturing_a_hotkey_owned_by_another_app_reports_it_and_keeps_the_previous_one()
    {
        var vm = Create();
        Hotkeys.Start();
        _registrar.TakenByOtherApps.Add(Custom);

        var ok = vm.TryCaptureHotkey(Custom);

        Assert.False(ok);
        Assert.Equal(HotkeyService.ConflictMessage, vm.HotkeyError);
        Assert.Equal(Hotkey.Default, _registrar.Current);
        Assert.Equal(Hotkey.Default, _store.Current.Hotkey);
        Assert.Equal("Ctrl+Alt+Space", vm.HotkeyText);
    }

    [Fact]
    public void Capturing_a_shortcut_without_a_modifier_is_refused()
    {
        var vm = Create();

        var ok = vm.TryCaptureHotkey(new Hotkey(HotkeyModifiers.None, 'A'));

        Assert.False(ok);
        Assert.NotNull(vm.HotkeyError);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public void Disable_unregisters_and_stores_the_hotkey_as_disabled()
    {
        _store.Current = new ShellSettings(Theme: ThemeChoice.Light);
        var vm = Create();
        Hotkeys.Start();

        vm.DisableHotkeyCommand.Execute(null);

        Assert.Null(_registrar.Current);
        Assert.Null(_store.Current.Hotkey);
        Assert.Equal(ThemeChoice.Light, _store.Current.Theme);
        Assert.Equal("Disabled", vm.HotkeyText);
    }

    [Fact]
    public void A_start_up_conflict_is_shown_when_Settings_opens()
    {
        _registrar.TakenByOtherApps.Add(Hotkey.Default);
        Hotkeys.Start();

        var vm = Create();

        Assert.Equal(HotkeyService.ConflictMessage, vm.HotkeyError);
    }
}
