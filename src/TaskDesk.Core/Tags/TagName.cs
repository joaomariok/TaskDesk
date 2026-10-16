namespace TaskDesk.Core.Tags;

/// <summary>Validation rule for a Tag name: 1 to 40 characters, no whitespace, no leading #.</summary>
public static class TagName
{
    public const int MaxLength = 40;

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxLength
        && name[0] != '#'
        && !name.Any(char.IsWhiteSpace);

    /// <exception cref="ArgumentException">The name breaks the Tag name rules.</exception>
    public static string Validate(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"A Tag name must have 1 to {MaxLength} characters, no spaces and no leading #.", nameof(name));
        }

        return name!;
    }

    /// <summary>The key used for case-insensitive uniqueness.</summary>
    public static string Normalize(string name) => name.ToUpperInvariant();
}
