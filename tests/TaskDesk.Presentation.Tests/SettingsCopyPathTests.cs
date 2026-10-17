using TaskDesk.Core.Paths;
using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class SettingsCopyPathTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Copied { get; } = [];
        public void SetText(string text) => Copied.Add(text);
    }

    private sealed class MemoryStore : IShellSettingsStore
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

    private static SettingsViewModel Create(IClipboardService? clipboard)
    {
        var store = new MemoryStore();
        return new SettingsViewModel(
            store, new AutoStartService(new NoRunKey()), new NoTheme(), new NoOpener(),
            AppPaths.ForRoot(@"C:\Data\TaskDesk"), @"C:\TaskDesk\TaskDesk.exe", "1.2.3",
            new HotkeyService(new FakeRegistrar(), store), clipboard: clipboard);
    }

    [Fact]
    public void Copy_data_folder_puts_the_exact_data_path_on_the_clipboard()
    {
        var clipboard = new FakeClipboard();
        var vm = Create(clipboard);

        vm.CopyDataFolderCommand.Execute(null);

        Assert.Equal([vm.DataFolder], clipboard.Copied);
    }

    [Fact]
    public void Copy_log_folder_puts_the_exact_log_path_on_the_clipboard()
    {
        var clipboard = new FakeClipboard();
        var vm = Create(clipboard);

        vm.CopyLogFolderCommand.Execute(null);

        Assert.Equal([vm.LogFolder], clipboard.Copied);
    }

    [Fact]
    public void Copying_without_a_clipboard_does_nothing()
    {
        var vm = Create(null);

        vm.CopyDataFolderCommand.Execute(null);
        vm.CopyLogFolderCommand.Execute(null);

        Assert.NotEmpty(vm.DataFolder);
    }
}
