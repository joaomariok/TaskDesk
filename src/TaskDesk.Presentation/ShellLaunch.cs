namespace TaskDesk.Presentation;

/// <summary>
/// The work the App hands to <see cref="ShellController.LaunchAsync"/> in one value: the window and the Run key
/// details, the exit parts, the callback that starts the system hooks, the Reminder activation that launched the
/// app, and the work that runs until the exit.
/// </summary>
public sealed record ShellLaunch
{
    /// <summary>Keep the main window hidden (a logon start, or a toast's Mark done or Snooze).</summary>
    public required bool Minimized { get; init; }

    /// <summary>The executable the Run key points at.</summary>
    public required string ExePath { get; init; }

    /// <summary>The exit work; <see cref="ShellController"/> runs it in a fixed order.</summary>
    public required ShellExitParts ExitParts { get; init; }

    /// <summary>
    /// Starts the session-end and resume hooks. Called once the exit parts are recorded and before the tray and the
    /// window, so nothing can start an exit before the parts are in place.
    /// </summary>
    public required Action StartSystemHooks { get; init; }

    /// <summary>The Reminder activation that launched the app, given <see cref="ShellController.ExitStarted"/>.</summary>
    public Func<CancellationToken, Task>? Activation { get; init; }

    /// <summary>Work that runs until the exit, such as the Reminder checks; started only while no exit has started.</summary>
    public required Action Running { get; init; }
}

/// <summary>
/// The exit work, by name. After the Reminder activations, <see cref="ShellController"/> runs the parts in the
/// order listed here, with the release of the tray and the single-instance guard between
/// <see cref="ReleaseHotkey"/> and <see cref="DisposeHost"/>. A failing part is logged, and the exit carries on.
/// </summary>
public sealed record ShellExitParts
{
    /// <summary>Takes no more input in the main window and stops its view models, which saves the pending edit.</summary>
    public required Func<Task> PrepareWindow { get; init; }

    /// <summary>Stops the host.</summary>
    public required Func<Task> StopHost { get; init; }

    /// <summary>
    /// Stops the resume and session-end hooks, their window, the Reminder scheduler, and unregisters notifications.
    /// </summary>
    public required Func<Task> StopSystemHooks { get; init; }

    /// <summary>Releases the global hotkey.</summary>
    public required Func<Task> ReleaseHotkey { get; init; }

    /// <summary>Disposes the host, which closes the database and the log.</summary>
    public required Func<Task> DisposeHost { get; init; }
}
