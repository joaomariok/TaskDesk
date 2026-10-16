using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>Backs the Settings window. Every change is applied and persisted immediately.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IShellSettingsStore _store;
    private readonly AutoStartService _autoStart;
    private readonly IThemeService _themeService;
    private readonly IFolderOpener _folderOpener;
    private readonly string _exePath;
    private readonly IBackupService? _backups;
    private readonly bool _loading = true;

    public SettingsViewModel(
        IShellSettingsStore store,
        AutoStartService autoStart,
        IThemeService themeService,
        IFolderOpener folderOpener,
        IAppPaths paths,
        string exePath,
        string version,
        IBackupService? backups = null)
    {
        _backups = backups;
        _store = store;
        _autoStart = autoStart;
        _themeService = themeService;
        _folderOpener = folderOpener;
        _exePath = exePath;
        DataFolder = paths.DataDirectory;
        LogFolder = paths.LogsDirectory;
        Version = version;

        var settings = store.Load();
        _theme = settings.Theme;
        _startWithWindows = settings.StartWithWindows;
        _closeBehavior = settings.CloseBehavior;
        _defaultReminderTimeText = FormatTime(settings.DefaultReminderTime);
        _loading = false;
    }

    public static IReadOnlyList<ThemeChoice> Themes { get; } = Enum.GetValues<ThemeChoice>();
    public static IReadOnlyList<CloseBehavior> CloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();

    public string Version { get; }
    public string DataFolder { get; }
    public string LogFolder { get; }

    [ObservableProperty]
    private ThemeChoice _theme;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private CloseBehavior _closeBehavior;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReminderTimeValid))]
    private string _defaultReminderTimeText;

    /// <summary>False while the text is not a valid HH:mm time; the last valid value stays saved.</summary>
    public bool IsReminderTimeValid => TryParseTime(DefaultReminderTimeText, out _);

    [RelayCommand]
    private void OpenDataFolder() => _folderOpener.Open(DataFolder);

    /// <summary>Outcome of the last "Back up now"; empty until one runs.</summary>
    [ObservableProperty]
    private string _backupStatus = "";

    [RelayCommand]
    private async Task BackUpNowAsync()
    {
        if (_backups is null)
        {
            return;
        }

        try
        {
            var path = await _backups.BackUpNowAsync();
            BackupStatus = $"Backed up to {path}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BackupStatus = $"Backup failed: {ex.Message}";
        }
    }

    partial void OnThemeChanged(ThemeChoice value)
    {
        if (_loading)
        {
            return;
        }

        _store.Save(_store.Load() with { Theme = value });
        _themeService.Apply(value);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _store.Save(_store.Load() with { StartWithWindows = value });
        _autoStart.Apply(value, _exePath);
    }

    partial void OnCloseBehaviorChanged(CloseBehavior value)
    {
        if (!_loading)
        {
            _store.Save(_store.Load() with { CloseBehavior = value });
        }
    }

    partial void OnDefaultReminderTimeTextChanged(string value)
    {
        if (!_loading && TryParseTime(value, out var time))
        {
            _store.Save(_store.Load() with { DefaultReminderTime = time });
        }
    }

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
