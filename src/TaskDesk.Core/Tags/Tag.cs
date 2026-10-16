namespace TaskDesk.Core.Tags;

/// <summary>A Tag: a short label attached to any number of Tasks. Shown with a leading #.</summary>
public sealed class Tag
{
    public Guid Id { get; set; }

    /// <summary>The name as first typed, without the leading #.</summary>
    public string Name { get; set; } = "";

    /// <summary>Upper-case copy of <see cref="Name"/>, used for case-insensitive uniqueness.</summary>
    public string NormalizedName { get; set; } = "";

    public string? ColorHex { get; set; }
}
