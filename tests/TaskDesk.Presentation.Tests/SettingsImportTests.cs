using TaskDesk.Core.Import;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class SettingsImportTests
{
    private sealed class FakeDialogs(string? path, ImportMode? mode, bool confirmReplace) : IImportDialogs
    {
        public List<string> Calls { get; } = [];

        public string? PickImportPath()
        {
            Calls.Add("pick");
            return path;
        }

        public ImportMode? ChooseImportMode(string chosenPath)
        {
            Calls.Add($"mode:{chosenPath}");
            return mode;
        }

        public bool ConfirmReplace()
        {
            Calls.Add("confirm");
            return confirmReplace;
        }
    }

    private sealed class FakeImports(Exception? failure = null) : IImportService
    {
        public List<(string Path, ImportMode Mode)> Imports { get; } = [];

        public Task<ImportResult> ImportJsonAsync(string path, ImportMode mode, CancellationToken cancellationToken = default)
        {
            Imports.Add((path, mode));
            return failure is not null
                ? Task.FromException<ImportResult>(failure)
                : Task.FromResult(new ImportResult(
                    mode, new ImportCounts(1, 2), new ImportCounts(3, 4), new ImportCounts(5, 6),
                    mode == ImportMode.Replace ? @"C:\Data\backups\taskdesk-20261016-093005.db" : null));
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

    private static SettingsViewModel Create(IImportService imports, IImportDialogs dialogs)
    {
        var store = new NoStore();
        return new SettingsViewModel(
            store, new AutoStartService(new NoRunKey()), new NoTheme(), new NoOpener(),
            AppPaths.ForRoot(@"C:\Data\TaskDesk"), @"C:\TaskDesk\TaskDesk.exe", "1.2.3",
            new HotkeyService(new FakeRegistrar(), store), imports: imports, importDialogs: dialogs);
    }

    [Fact]
    public async Task Merge_imports_without_asking_for_replace_confirmation_and_reports_the_counts()
    {
        var imports = new FakeImports();
        var dialogs = new FakeDialogs(@"C:\in\a.json", ImportMode.Merge, confirmReplace: false);
        var vm = Create(imports, dialogs);
        var imported = 0;
        vm.Imported += () => imported++;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["pick", @"mode:C:\in\a.json"], dialogs.Calls);
        Assert.Equal([(@"C:\in\a.json", ImportMode.Merge)], imports.Imports);
        Assert.Equal("Merged: 5 tasks added, 6 skipped; 1 lists added, 2 skipped; 3 tags added, 4 skipped.", vm.ImportStatus);
        Assert.Equal(1, imported);
    }

    [Fact]
    public async Task Replace_asks_for_confirmation_then_replaces_and_names_the_backup()
    {
        var imports = new FakeImports();
        var dialogs = new FakeDialogs(@"C:\in\a.json", ImportMode.Replace, confirmReplace: true);
        var vm = Create(imports, dialogs);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["pick", @"mode:C:\in\a.json", "confirm"], dialogs.Calls);
        Assert.Equal([(@"C:\in\a.json", ImportMode.Replace)], imports.Imports);
        Assert.Contains("Replaced", vm.ImportStatus);
        Assert.Contains(@"C:\Data\backups\taskdesk-20261016-093005.db", vm.ImportStatus);
    }

    [Fact]
    public async Task Declining_the_replace_confirmation_imports_nothing()
    {
        var imports = new FakeImports();
        var vm = Create(imports, new FakeDialogs(@"C:\in\a.json", ImportMode.Replace, confirmReplace: false));
        var imported = 0;
        vm.Imported += () => imported++;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(imports.Imports);
        Assert.Equal("", vm.ImportStatus);
        Assert.Equal(0, imported);
    }

    [Theory]
    [InlineData(null, ImportMode.Merge)]
    [InlineData(@"C:\in\a.json", null)]
    public async Task Cancelling_the_file_or_mode_dialog_imports_nothing(string? path, ImportMode? mode)
    {
        var imports = new FakeImports();
        var vm = Create(imports, new FakeDialogs(path, mode, confirmReplace: true));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(imports.Imports);
        Assert.Equal("", vm.ImportStatus);
    }

    [Fact]
    public async Task A_rejected_file_is_reported_not_thrown_and_nothing_is_refreshed()
    {
        var vm = Create(
            new FakeImports(new ImportException("The file is not a TaskDesk export.")),
            new FakeDialogs(@"C:\in\a.json", ImportMode.Merge, confirmReplace: false));
        var imported = 0;
        vm.Imported += () => imported++;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Import failed: The file is not a TaskDesk export.", vm.ImportStatus);
        Assert.Equal(0, imported);
    }
}
