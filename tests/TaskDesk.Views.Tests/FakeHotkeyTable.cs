using TaskDesk.App.Shell;

namespace TaskDesk.Views.Tests;

/// <summary>
/// The session's global hotkey table as Win32 RegisterHotKey keeps it, for the registrars of one test: a combination
/// belongs to one registration (window and id) until it is unregistered, and anybody else is refused it. The real
/// table is shared with every app on the desktop, so the gate never registers a real hotkey; the UI suite's
/// GlobalHotkeyTests do.
/// </summary>
internal sealed class FakeHotkeyTable : IWin32Hotkeys
{
    private const uint ModNoRepeat = 0x4000;

    private readonly Dictionary<(uint Modifiers, uint Key), (nint Window, int Id)> _owners = [];

    /// <summary>Every RegisterHotKey call, as Win32 received it.</summary>
    public List<(nint Window, int Id, uint Modifiers, uint Key)> Registrations { get; } = [];

    public bool TryRegister(nint window, int id, uint modifiers, uint virtualKey, out string error)
    {
        Registrations.Add((window, id, modifiers, virtualKey));
        var combination = (modifiers & ~ModNoRepeat, virtualKey);
        if (_owners.ContainsKey(combination))
        {
            error = "Hot key is already registered.";
            return false;
        }

        _owners[combination] = (window, id);
        error = "";
        return true;
    }

    public void Unregister(nint window, int id)
    {
        foreach (var owned in _owners.Where(o => o.Value == (window, id)).ToList())
        {
            _owners.Remove(owned.Key);
        }
    }
}
