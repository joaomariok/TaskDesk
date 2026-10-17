using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Backups;
using TaskDesk.Core.Export;
using TaskDesk.Core.Import;
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
    private readonly HotkeyService _hotkeys;
    private readonly IBackupService? _backups;
    private readonly IExportService? _exports;
    private readonly IExportDialogs? _exportDialogs;
    private readonly TimeProvider _clock;
    private readonly IImportService? _imports;
    private readonly IImportDialogs? _importDialogs;
    private readonly IClipboardService? _clipboard;
    private readonly bool _loading = true;

    public SettingsViewModel(
        IShellSettingsStore store,
        AutoStartService autoStart,
        IThemeService themeService,
        IFolderOpener folderOpener,
        IAppPaths paths,
        string exePath,
        string version,
        HotkeyService hotkeys,
        IBackupService? backups = null,
        IExportService? exports = null,
        IExportDialogs? exportDialogs = null,
        TimeProvider? clock = null,
        IImportService? imports = null,
        IImportDialogs? importDialogs = null,
        IClipboardService? clipboard = null)
    {
        _clipboard = clipboard;
        _imports = imports;
        _importDialogs = importDialogs;
        _exports = exports;
        _exportDialogs = exportDialogs;
        _clock = clock ?? TimeProvider.System;
        _backups = backups;
        _hotkeys = hotkeys;
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
        _hotkeyText = FormatHotkey(settings.Hotkey);
        _hotkeyError = hotkeys.Error;
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

    /// <summary>The global quick-add shortcut as text, or "Disabled".</summary>
    [ObservableProperty]
    private string _hotkeyText;

    /// <summary>Why the shortcut could not be used (for example, owned by another app); null when fine.</summary>
    [ObservableProperty]
    private string? _hotkeyError;

    /// <summary>
    /// Applies a captured shortcut and stores it. On failure nothing is saved and the previous
    /// working shortcut stays active; <see cref="HotkeyError"/> says why.
    /// </summary>
    public bool TryCaptureHotkey(Hotkey hotkey)
    {
        if (!hotkey.IsValid)
        {
            HotkeyError = "Use Ctrl, Alt, Shift or Win with a letter, digit, F-key, Space or navigation key.";
            return false;
        }

        if (!_hotkeys.Apply(hotkey))
        {
            HotkeyError = _hotkeys.Error;
            return false;
        }

        _store.Save(_store.Load() with { Hotkey = hotkey });
        HotkeyText = FormatHotkey(hotkey);
        HotkeyError = null;
        return true;
    }

    [RelayCommand]
    private void DisableHotkey()
    {
        _hotkeys.Apply(null);
        _store.Save(_store.Load() with { Hotkey = null });
        HotkeyText = FormatHotkey(null);
        HotkeyError = null;
    }

    [RelayCommand]
    private void CopyDataFolder() => _clipboard?.SetText(DataFolder);

    [RelayCommand]
    private void CopyLogFolder() => _clipboard?.SetText(LogFolder);

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

    /// <summary>Outcome of the last export; empty until one runs.</summary>
    [ObservableProperty]
    private string _exportStatus = "";

    [RelayCommand]
    private Task ExportJsonAsync() => ExportAsync(ExportFormat.Json);

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(ExportFormat.Csv);

    private async Task ExportAsync(ExportFormat format)
    {
        if (_exports is null || _exportDialogs is null)
        {
            return;
        }

        var name = ExportNaming.SuggestedFileName(_clock.GetLocalNow().DateTime, format);
        var path = _exportDialogs.PickExportPath(format, name);
        if (path is null)
        {
            return;
        }

        try
        {
            if (format == ExportFormat.Json)
            {
                await _exports.ExportJsonAsync(path, Version);
            }
            else
            {
                await _exports.ExportCsvAsync(path);
            }

            ExportStatus = $"Exported to {path}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Raised after an import changed the data, so open views can reload.</summary>
    public event Action? Imported;

    /// <summary>
    /// Runs after the owner confirmed an import and before it starts: the main window saves its pending
    /// detail edit and closes the detail pane, so no edit of the old data lands on the imported data.
    /// </summary>
    public Func<Task>? BeforeImport { get; set; }

    /// <summary>Outcome of the last import; empty until one runs.</summary>
    [ObservableProperty]
    private string _importStatus = "";

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_imports is null || _importDialogs is null)
        {
            return;
        }

        var path = _importDialogs.PickImportPath();
        if (path is null || _importDialogs.ChooseImportMode(path) is not { } mode)
        {
            return;
        }

        if (mode == ImportMode.Replace && !_importDialogs.ConfirmReplace())
        {
            return;
        }

        if (BeforeImport is { } prepare)
        {
            await prepare();
        }

        try
        {
            var result = await _imports.ImportJsonAsync(path, mode);
            ImportStatus = Describe(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ImportStatus = $"Import failed: {ex.Message}";
            return;
        }

        Imported?.Invoke();
    }

    private static string Describe(ImportResult r) => r.Mode == ImportMode.Merge
        ? $"Merged: {r.Tasks.Added} tasks added, {r.Tasks.Skipped} skipped; "
            + $"{r.Lists.Added} lists added, {r.Lists.Skipped} skipped; "
            + $"{r.Tags.Added} tags added, {r.Tags.Skipped} skipped."
        : $"Replaced all data with {r.Tasks.Added} tasks, {r.Lists.Added} lists and {r.Tags.Added} tags. "
            + $"Previous data backed up to {r.BackupPath}";

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

    private static string FormatHotkey(Hotkey? hotkey) => hotkey?.ToString() ?? "Disabled";

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
