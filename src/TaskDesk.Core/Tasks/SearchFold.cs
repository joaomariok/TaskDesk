namespace TaskDesk.Core.Tasks;

/// <summary>
/// The one case-folding rule of search: invariant lower case, so "ÂNGELA" finds "ângela". The database search
/// and the Task list's open-by-search both use it. SQLite's own LIKE and lower() fold ASCII letters only.
/// </summary>
public static class SearchFold
{
    /// <summary>The folded form of <paramref name="text"/> (invariant lower case); null stays null.</summary>
    public static string? Apply(string? text) => text?.ToLowerInvariant();

    /// <summary>True when <paramref name="text"/> contains <paramref name="search"/>, ignoring case by the fold.</summary>
    public static bool Contains(string? text, string? search) =>
        Apply(text) is { } folded && Apply(search) is { } needle && folded.Contains(needle, StringComparison.Ordinal);
}
