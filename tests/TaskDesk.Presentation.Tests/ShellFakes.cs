using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

// The shell's ports, faked for ShellController and the view models that drive its exit.
internal sealed class FakeTray : ITrayIcon
{
    public event Action? LeftClicked;
    public event Action? OpenRequested;
    public event Action? QuickAddRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? RestartToUpdateRequested;

    public bool Shown { get; private set; }
    public int DisposeCount { get; private set; }
    public List<string> Hints { get; } = [];

    public void Show() => Shown = true;
    public void ShowHint(string message) => Hints.Add(message);
    public void Dispose() => DisposeCount++;

    public void ClickLeft() => LeftClicked?.Invoke();
    public void ClickOpen() => OpenRequested?.Invoke();
    public void ClickQuickAdd() => QuickAddRequested?.Invoke();
    public void ClickSettings() => SettingsRequested?.Invoke();
    public void ClickExit() => ExitRequested?.Invoke();
    public void ClickRestartToUpdate() => RestartToUpdateRequested?.Invoke();
}

internal sealed class FakeWindow : IMainWindow
{
    public bool IsVisible { get; private set; }
    public int FocusCount { get; private set; }

    public void ShowAndFocus()
    {
        IsVisible = true;
        FocusCount++;
    }

    public void Hide() => IsVisible = false;
}

internal sealed class FakeSingleInstance : ISingleInstance
{
    public bool IsFirst { get; set; } = true;
    public int SignalCount { get; private set; }
    public List<IReadOnlyList<string>> Signals { get; } = [];
    public int DisposeCount { get; private set; }
    public Action<IReadOnlyList<string>>? OnSecondLaunch { get; private set; }

    public bool TryAcquire() => IsFirst;
    public Task SignalExistingAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        SignalCount++;
        Signals.Add(arguments);
        return Task.CompletedTask;
    }

    public void Listen(Action<IReadOnlyList<string>> onSecondLaunch) => OnSecondLaunch = onSecondLaunch;
    public void Dispose() => DisposeCount++;
}

internal sealed class FakeLifetime : IAppShutdown
{
    public int ShutdownCount { get; private set; }
    public void Shutdown() => ShutdownCount++;
}
