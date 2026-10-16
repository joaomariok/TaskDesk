namespace TaskDesk.Core.Lists;

/// <summary>Validation rule for a List name: trimmed, 1 to 100 characters.</summary>
public static class ListName
{
    public const int MaxLength = 100;

    public static bool IsValid(string? name)
    {
        var trimmed = name?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaxLength;
    }

    /// <exception cref="ArgumentException">The name is empty or longer than <see cref="MaxLength"/>.</exception>
    public static string Normalize(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"A List name must have 1 to {MaxLength} characters after trimming.", nameof(name));
        }

        return name!.Trim();
    }
}
