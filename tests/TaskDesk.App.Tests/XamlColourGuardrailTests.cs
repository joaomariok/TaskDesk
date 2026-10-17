using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TaskDesk.App.Tests;

/// <summary>
/// Guardrail: XAML under src takes every colour from theme resources. The only literal
/// colours allowed are the six List presets, used as Tag values; Transparent is allowed
/// because it is not a colour choice (it makes an element hit-testable).
/// </summary>
public sealed partial class XamlColourGuardrailTests
{
    private static readonly string[] ListPresets =
        ["#D13438", "#F7630C", "#FFB900", "#107C10", "#0078D4", "#744DA9"];

    private const string ColourAttributes =
        "Foreground|Background|BorderBrush|Fill|Stroke|Color|CaretBrush|SelectionBrush|OpacityMask";

    // Any attribute whose value is a hex colour.
    [GeneratedRegex("\\b(?<name>[\\w:.]+)\\s*=\\s*\"(?<value>#[0-9A-Fa-f]{3,8})\"")]
    private static partial Regex HexAttribute();

    // A colour-taking attribute whose value is a bare word (named colour), not a {markup extension}.
    [GeneratedRegex("\\b(?:" + ColourAttributes + ")\\s*=\\s*\"(?<value>[^\"{#][^\"]*)\"")]
    private static partial Regex NamedColourAttribute();

    [GeneratedRegex("<Setter\\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex SetterElement();

    [GeneratedRegex("\\bProperty\\s*=\\s*\"(?:[\\w.]*\\.)?(?:" + ColourAttributes + ")\"")]
    private static partial Regex ColourSetterProperty();

    [GeneratedRegex("\\bValue\\s*=\\s*\"(?<value>[^\"{#][^\"]*)\"")]
    private static partial Regex LiteralSetterValue();

    [Fact]
    public void Xaml_under_src_contains_no_literal_colours()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var offences = new List<string>();
        var files = Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f, src));

        foreach (var file in files)
        {
            offences.AddRange(Scan(File.ReadAllText(file))
                .Select(o => $"{Path.GetRelativePath(src, file)}:{o}"));
        }

        Assert.True(offences.Count == 0,
            "Literal colours found in XAML; use theme resources instead:\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("<TextBlock Foreground=\"Red\" />", 1)]
    [InlineData("<Border Background=\"#FFF\" />", 1)]
    [InlineData("<Border BorderBrush=\"#80112233\" />", 1)]
    [InlineData("<Setter Property=\"Foreground\" Value=\"Goldenrod\" />", 1)]
    [InlineData("<Setter Property=\"TextElement.Foreground\" Value=\"White\" />", 1)]
    [InlineData("<Setter Property=\"Background\" Value=\"#112233\" />", 1)]
    [InlineData("<GradientStop Color=\"Black\" />", 1)]
    [InlineData("<MenuItem Tag=\"#D13438\" />", 0)]
    [InlineData("<MenuItem Tag=\"#123456\" />", 1)]
    [InlineData("<Border Background=\"Transparent\" />", 0)]
    [InlineData("<Border Background=\"{DynamicResource Surface}\" />", 0)]
    [InlineData("<Setter Property=\"Foreground\" Value=\"{DynamicResource Text}\" />", 0)]
    [InlineData("<Setter Property=\"Margin\" Value=\"Red\" />", 0)]
    [InlineData("<MenuItem Header=\"Red\" />", 0)]
    public void Scanner_flags_literal_colours(string xaml, int expected)
    {
        Assert.Equal(expected, Scan(xaml).Count());
    }

    private static IEnumerable<string> Scan(string text)
    {
        foreach (Match m in HexAttribute().Matches(text))
        {
            var isPreset = m.Groups["name"].Value == "Tag"
                && ListPresets.Contains(m.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
            if (!isPreset)
            {
                yield return Describe(text, m);
            }
        }

        foreach (Match m in NamedColourAttribute().Matches(text))
        {
            if (!IsAllowedWord(m.Groups["value"].Value))
            {
                yield return Describe(text, m);
            }
        }

        foreach (Match setter in SetterElement().Matches(text))
        {
            if (!ColourSetterProperty().IsMatch(setter.Value))
            {
                continue;
            }

            // Hex setter values are already reported by HexAttribute.
            var value = LiteralSetterValue().Match(setter.Value);
            if (value.Success && !IsAllowedWord(value.Groups["value"].Value))
            {
                yield return Describe(text, setter);
            }
        }
    }

    private static bool IsAllowedWord(string value) =>
        string.Equals(value, "Transparent", StringComparison.OrdinalIgnoreCase);

    private static string Describe(string text, Match m)
    {
        var line = text.AsSpan(0, m.Index).Count('\n') + 1;
        return $"{line}: {m.Value.Trim()}";
    }

    private static bool IsBuildOutput(string path, string src)
    {
        var parts = Path.GetRelativePath(src, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj") || parts.Contains("bin");
    }

    // Recorded at build time (RepoRoot in the project file), so it holds wherever the build output goes.
    private static string FindRepoRoot() =>
        typeof(XamlColourGuardrailTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepoRoot").Value
        ?? throw new InvalidOperationException("The RepoRoot assembly metadata has no value.");
}
