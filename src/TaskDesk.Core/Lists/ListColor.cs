using System.Text.RegularExpressions;

namespace TaskDesk.Core.Lists;

/// <summary>Validation rule for a List colour: null (none) or #RRGGBB.</summary>
public static partial class ListColor
{
    public static bool IsValid(string? colorHex) => colorHex is null || HexPattern().IsMatch(colorHex);

    /// <exception cref="ArgumentException">The value is neither null nor #RRGGBB.</exception>
    public static string? Normalize(string? colorHex)
    {
        if (!IsValid(colorHex))
        {
            throw new ArgumentException("A List colour must be null or #RRGGBB.", nameof(colorHex));
        }

        return colorHex?.ToUpperInvariant();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();
}
