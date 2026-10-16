using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation.Tests;

/// <summary>Fake of the Win32 hotkey registration; combinations in <see cref="TakenByOtherApps"/> fail to register.</summary>
internal sealed class FakeRegistrar : IHotkeyRegistrar
{
    public event Action? Pressed;

    public HashSet<Hotkey> TakenByOtherApps { get; } = [];
    public Hotkey? Current { get; private set; }
    public int DisposeCount { get; private set; }

    public bool TryRegister(Hotkey hotkey)
    {
        if (TakenByOtherApps.Contains(hotkey))
        {
            return false;
        }

        Current = hotkey;
        return true;
    }

    public void Unregister() => Current = null;

    public void Press() => Pressed?.Invoke();

    public void Dispose() => DisposeCount++;
}
