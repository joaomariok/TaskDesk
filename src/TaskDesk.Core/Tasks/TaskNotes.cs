namespace TaskDesk.Core.Tasks;

/// <summary>Validation rule for Task notes: plain text, at most 10,000 characters; blank means none.</summary>
public static class TaskNotes
{
    public const int MaxLength = 10_000;

    public static bool IsValid(string? notes) => notes is null || notes.Length <= MaxLength;

    /// <summary>Returns the notes as stored: null when blank, otherwise unchanged.</summary>
    /// <exception cref="ArgumentException">The notes are longer than <see cref="MaxLength"/>.</exception>
    public static string? Normalize(string? notes)
    {
        if (!IsValid(notes))
        {
            throw new ArgumentException($"Task notes can have at most {MaxLength} characters.", nameof(notes));
        }

        return string.IsNullOrWhiteSpace(notes) ? null : notes;
    }
}
