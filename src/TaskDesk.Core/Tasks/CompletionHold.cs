namespace TaskDesk.Core.Tasks;

/// <summary>
/// How long a just-completed Task stays in place, struck through and dimmed, before it moves into
/// "Completed (N)": a moment, or none at all when Windows asks for less animation.
/// </summary>
public static class CompletionHold
{
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(400);

    /// <param name="animationsEnabled">Windows' "Animation effects" setting.</param>
    public static TimeSpan For(bool animationsEnabled) => animationsEnabled ? Hold : TimeSpan.Zero;
}
