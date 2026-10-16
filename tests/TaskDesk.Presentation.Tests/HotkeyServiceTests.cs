using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

public sealed class HotkeyServiceTests
{
    private sealed class FakeSettings : IShellSettingsStore
    {
        public ShellSettings Current { get; set; } = new();
        public ShellSettings Load() => Current;
        public void Save(ShellSettings settings) => Current = settings;
    }

    private static readonly Hotkey Other = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'K');

    private readonly FakeRegistrar _registrar = new();
    private readonly FakeSettings _settings = new();

    private HotkeyService Create() => new(_registrar, _settings);

    [Fact]
    public void Start_registers_the_default_hotkey()
    {
        Create().Start();

        Assert.Equal(Hotkey.Default, _registrar.Current);
    }

    [Fact]
    public void Start_registers_the_stored_hotkey()
    {
        _settings.Current = new ShellSettings { Hotkey = Other };

        Create().Start();

        Assert.Equal(Other, _registrar.Current);
    }

    [Fact]
    public void Start_registers_nothing_when_disabled()
    {
        _settings.Current = new ShellSettings { Hotkey = null };

        Create().Start();

        Assert.Null(_registrar.Current);
    }

    [Fact]
    public void Start_with_a_taken_hotkey_does_not_throw_and_reports_the_conflict()
    {
        _registrar.TakenByOtherApps.Add(Hotkey.Default);
        var service = Create();

        service.Start();

        Assert.Null(_registrar.Current);
        Assert.Equal(HotkeyService.ConflictMessage, service.Error);
    }

    [Fact]
    public void Pressing_the_hotkey_raises_triggered()
    {
        var service = Create();
        service.Start();
        var count = 0;
        service.Triggered += () => count++;

        _registrar.Press();

        Assert.Equal(1, count);
    }

    [Fact]
    public void A_conflicting_change_keeps_the_previous_hotkey_and_reports_it()
    {
        var service = Create();
        service.Start();
        _registrar.TakenByOtherApps.Add(Other);

        var applied = service.Apply(Other);

        Assert.False(applied);
        Assert.Equal(Hotkey.Default, _registrar.Current);
        Assert.Equal(HotkeyService.ConflictMessage, service.Error);
    }

    [Fact]
    public void A_successful_change_replaces_the_hotkey_and_clears_the_error()
    {
        var service = Create();
        _registrar.TakenByOtherApps.Add(Hotkey.Default);
        service.Start();

        var applied = service.Apply(Other);

        Assert.True(applied);
        Assert.Equal(Other, _registrar.Current);
        Assert.Null(service.Error);
    }

    [Fact]
    public void Applying_null_unregisters()
    {
        var service = Create();
        service.Start();

        service.Apply(null);

        Assert.Null(_registrar.Current);
    }

    [Fact]
    public void Dispose_disposes_the_registrar_and_stops_forwarding_presses()
    {
        var service = Create();
        service.Start();
        var count = 0;
        service.Triggered += () => count++;

        service.Dispose();
        _registrar.Press();

        Assert.Equal(1, _registrar.DisposeCount);
        Assert.Equal(0, count);
    }
}
