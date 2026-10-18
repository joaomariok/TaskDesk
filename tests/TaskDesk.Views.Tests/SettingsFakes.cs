using TaskDesk.Core.Export;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;

namespace TaskDesk.Views.Tests;

/// <summary>Settings held in memory, so nothing touches settings.json.</summary>
internal sealed class InMemorySettingsStore : IShellSettingsStore
{
    public ShellSettings Current { get; set; } = new();

    public ShellSettings Load() => Current;

    public void Save(ShellSettings settings) => Current = settings;
}

/// <summary>A Run key in memory, so nothing touches the registry.</summary>
internal sealed class InMemoryRunKey : IRunKey
{
    public Dictionary<string, string> Values { get; } = [];

    public string? GetValue(string name) => Values.GetValueOrDefault(name);

    public void SetValue(string name, string value) => Values[name] = value;

    public void Remove(string name) => Values.Remove(name);
}

internal sealed class RecordingFolderOpener : IFolderOpener
{
    public List<string> Opened { get; } = [];

    public void Open(string path) => Opened.Add(path);
}

internal sealed class RecordingClipboard : IClipboardService
{
    public List<string> Copied { get; } = [];

    public void SetText(string text) => Copied.Add(text);
}

/// <summary>The save picker answered with a fixed path, and an export that only records where it wrote.</summary>
internal sealed class FakeExport(string pickedPath) : IExportDialogs, IExportService
{
    public List<string> Written { get; } = [];

    public Task<string?> PickExportPathAsync(ExportFormat format, string suggestedFileName) =>
        Task.FromResult<string?>(pickedPath);

    public Task ExportJsonAsync(string path, string appVersion, CancellationToken cancellationToken = default)
    {
        Written.Add(path);
        return Task.CompletedTask;
    }

    public Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        Written.Add(path);
        return Task.CompletedTask;
    }
}
