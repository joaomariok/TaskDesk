using TaskDesk.Core.Shell;

namespace TaskDesk.Presentation;

/// <summary>Applies the configured global hotkey and keeps the last registration problem for Settings.</summary>
public sealed class HotkeyService(IHotkeyRegistrar registrar, IShellSettingsStore settingsStore) : IDisposable
{
    public const string ConflictMessage = "This shortcut is used by another application.";

    private bool _started;

    /// <summary>Raised when the registered hotkey is pressed.</summary>
    public event Action? Triggered;

    /// <summary>Why the last attempt to register failed; null when it succeeded or the hotkey is disabled.</summary>
    public string? Error { get; private set; }

    /// <summary>Registers the stored hotkey. A conflict is kept in <see cref="Error"/>, never thrown.</summary>
    public void Start()
    {
        if (!_started)
        {
            _started = true;
            registrar.Pressed += OnPressed;
        }

        Apply(settingsStore.Load().Hotkey);
    }

    /// <summary>
    /// Switches to <paramref name="hotkey"/> (null disables it). Returns false when another application
    /// owns the combination; the previously working hotkey then stays registered.
    /// </summary>
    public bool Apply(Hotkey? hotkey)
    {
        if (hotkey is null)
        {
            registrar.Unregister();
            Error = null;
            return true;
        }

        if (!registrar.TryRegister(hotkey))
        {
            Error = ConflictMessage;
            return false;
        }

        Error = null;
        return true;
    }

    public void Dispose()
    {
        registrar.Pressed -= OnPressed;
        registrar.Dispose();
    }

    private void OnPressed() => Triggered?.Invoke();
}
