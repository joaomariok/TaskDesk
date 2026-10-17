using Microsoft.Extensions.Time.Testing;
using TaskDesk.Core.Export;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class SettingsExportTests
{
    private sealed class FakeDialogs(string? answer) : IExportDialogs
    {
        public List<(ExportFormat Format, string Name)> Asked { get; } = [];

        public string? PickExportPath(ExportFormat format, string suggestedFileName)
        {
            Asked.Add((format, suggestedFileName));
            return answer;
        }
    }

    private sealed class FakeExports(Exception? failure = null) : IExportService
    {
        public List<(string Path, string Version)> Json { get; } = [];
        public List<string> Csv { get; } = [];

        public Task ExportJsonAsync(string path, string appVersion, CancellationToken cancellationToken = default)
        {
            Json.Add((path, appVersion));
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }

        public Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
        {
            Csv.Add(path);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class NoStore : IShellSettingsStore
    {
        public ShellSettings Load() => new();
        public void Save(ShellSettings settings)
        {
        }
    }

    private sealed class NoRunKey : IRunKey
    {
        public string? GetValue(string name) => null;
        public void SetValue(string name, string value)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class NoTheme : IThemeService
    {
        public void Apply(ThemeChoice theme)
        {
        }
    }

    private sealed class NoOpener : IFolderOpener
    {
        public void Open(string path)
        {
        }
    }

    private static SettingsViewModel Create(IExportService exports, IExportDialogs dialogs)
    {
        var store = new NoStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 9, 30, 5, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new SettingsViewModel(
            store, new AutoStartService(new NoRunKey()), new NoTheme(), new NoOpener(),
            AppPaths.ForRoot(@"C:\Data\TaskDesk"), @"C:\TaskDesk\TaskDesk.exe", "1.2.3",
            new HotkeyService(new FakeRegistrar(), store), null, exports, dialogs, clock);
    }

    [Fact]
    public async Task Export_to_JSON_asks_for_a_path_then_exports_with_the_app_version()
    {
        var exports = new FakeExports();
        var dialogs = new FakeDialogs(@"C:\out\a.json");
        var vm = Create(exports, dialogs);

        await vm.ExportJsonCommand.ExecuteAsync(null);

        Assert.Equal([(ExportFormat.Json, "taskdesk-export-20261016-093005.json")], dialogs.Asked);
        Assert.Equal([(@"C:\out\a.json", "1.2.3")], exports.Json);
        Assert.Empty(exports.Csv);
        Assert.Contains(@"C:\out\a.json", vm.ExportStatus);
    }

    [Fact]
    public async Task Export_to_CSV_asks_for_a_path_then_exports()
    {
        var exports = new FakeExports();
        var dialogs = new FakeDialogs(@"C:\out\a.csv");
        var vm = Create(exports, dialogs);

        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.Equal([(ExportFormat.Csv, "taskdesk-export-20261016-093005.csv")], dialogs.Asked);
        Assert.Equal([@"C:\out\a.csv"], exports.Csv);
        Assert.Empty(exports.Json);
    }

    [Fact]
    public async Task Cancelling_the_dialog_exports_nothing_and_says_nothing()
    {
        var exports = new FakeExports();
        var vm = Create(exports, new FakeDialogs(null));

        await vm.ExportJsonCommand.ExecuteAsync(null);
        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.Empty(exports.Json);
        Assert.Empty(exports.Csv);
        Assert.Equal("", vm.ExportStatus);
    }

    [Fact]
    public async Task A_failed_export_is_reported_not_thrown()
    {
        var vm = Create(new FakeExports(new IOException("disk full")), new FakeDialogs(@"C:\out\a.json"));

        await vm.ExportJsonCommand.ExecuteAsync(null);

        Assert.Contains("disk full", vm.ExportStatus);
    }
}
