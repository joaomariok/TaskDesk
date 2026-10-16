namespace TaskDesk.Core.Tasks;

/// <summary>Validation rule for a Task title: trimmed, 1 to 500 characters.</summary>
public static class TaskTitle
{
    public const int MaxLength = 500;

    public static bool IsValid(string? title)
    {
        var trimmed = title?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaxLength;
    }

    /// <exception cref="ArgumentException">The title is empty or longer than <see cref="MaxLength"/>.</exception>
    public static string Normalize(string? title)
    {
        if (!IsValid(title))
        {
            throw new ArgumentException(
                $"A Task title must have 1 to {MaxLength} characters after trimming.", nameof(title));
        }

        return title!.Trim();
    }
}
