using System.Reflection;
using System.Text.RegularExpressions;

namespace TaskDesk.Core.Tests;

/// <summary>
/// Guardrail for the WinUI port (ADR 0004): XAML in a WinUI project (a project file with
/// <c>UseWinUI</c> set to true) uses <c>x:Bind</c> and <c>ThemeResource</c> only, so it stays AOT-ready.
/// Each x:Bind function call over a property path also takes a FallbackValue, since x:Bind skips the call when
/// an argument is null. WPF XAML is not scanned. Until the WinUI projects exist there is nothing to scan and the test passes.
/// </summary>
public sealed partial class WinUiXamlGuardrailTests
{
    [GeneratedRegex("\\{\\s*Binding\\b")]
    private static partial Regex RuntimeBinding();

    [GeneratedRegex("\\{\\s*DynamicResource\\b")]
    private static partial Regex DynamicResourceReference();

    [GeneratedRegex("<UseWinUI>\\s*true\\s*</UseWinUI>", RegexOptions.IgnoreCase)]
    private static partial Regex UseWinUiProperty();

    [Fact]
    public void WinUi_xaml_under_src_has_no_runtime_bindings_or_dynamic_resources()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var offences = WinUiXamlFiles(src)
            .SelectMany(file => Scan(File.ReadAllText(file)).Select(o => $"{Path.GetRelativePath(src, file)}:{o}"))
            .ToList();

        Assert.True(offences.Count == 0,
            "WinUI XAML must use x:Bind and ThemeResource (ADR 0004):\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("<TextBlock Text=\"{Binding Title}\" />", 1)]
    [InlineData("<TextBlock Text=\"{ Binding Title}\" />", 1)]
    [InlineData("<TextBlock Text=\"{Binding}\" />", 1)]
    [InlineData("<Border Background=\"{DynamicResource Surface}\" />", 1)]
    [InlineData("<Setter Property=\"Foreground\" Value=\"{DynamicResource Text}\" />", 1)]
    [InlineData("<Border Background=\"{ThemeResource Surface}\" />", 0)]
    [InlineData("<TextBlock Text=\"{x:Bind ViewModel.Title, Mode=OneWay}\" />", 0)]
    [InlineData("<TextBlock Text=\"{x:Bind BindingHelper.Title}\" />", 0)]
    [InlineData("<TextBlock Text=\"{Binding A}\" Foreground=\"{DynamicResource B}\" />", 2)]
    public void Scanner_flags_runtime_bindings_and_dynamic_resources(string xaml, int expected)
    {
        Assert.Equal(expected, Scan(xaml).Count());
    }

    [Fact]
    public void Only_xaml_of_WinUI_projects_is_scanned()
    {
        var root = Directory.CreateTempSubdirectory("taskdesk-tests-").FullName;
        try
        {
            AddProject(root, "Wpf", "<UseWPF>true</UseWPF>", "Views/Main.xaml", "<Grid Tag=\"{Binding X}\" />");
            AddProject(root, "WinUi", "<UseWinUI>true</UseWinUI>", "Views/Main.xaml", "<Grid Tag=\"{Binding X}\" />");
            AddProject(root, "WinUi", "<UseWinUI>true</UseWinUI>", "obj/Generated.xaml", "<Grid />");
            AddProject(root, "Plain", "", "Other.xaml", "<Grid />");

            var scanned = WinUiXamlFiles(root)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToList();

            Assert.Equal(["WinUi/Views/Main.xaml"], scanned);
            Assert.Single(WinUiXamlFiles(root).SelectMany(f => Scan(File.ReadAllText(f))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_tree_without_WinUI_projects_scans_nothing()
    {
        var root = Directory.CreateTempSubdirectory("taskdesk-tests-").FullName;
        try
        {
            AddProject(root, "Wpf", "<UseWPF>true</UseWPF>", "Main.xaml", "<Grid Tag=\"{DynamicResource X}\" />");

            Assert.Empty(WinUiXamlFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WinUi_xaml_under_src_gives_each_x_Bind_function_over_a_path_a_FallbackValue()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var offences = WinUiXamlFiles(src)
            .SelectMany(file => ScanFunctionBindings(File.ReadAllText(file))
                .Select(o => $"{Path.GetRelativePath(src, file)}:{o}"))
            .ToList();

        Assert.True(offences.Count == 0,
            "x:Bind skips a function whose argument is null, leaving the old value; give each a FallbackValue "
            + "equal to what the function returns for null:\n" + string.Join('\n', offences));
    }

    [Theory]
    [InlineData("<Ellipse Fill=\"{x:Bind local:TaskRow.StripeBrush(Row.ListColorHex), Mode=OneWay}\" />", 1)]
    [InlineData("<Ellipse Fill=\"{x:Bind local:TaskRow.StripeBrush(Row.ListColorHex), Mode=OneWay, FallbackValue={x:Null}}\" />", 0)]
    [InlineData("<TextBlock Visibility=\"{x:Bind Shown(ViewModel.Error), Mode=OneWay, FallbackValue=Collapsed}\" />", 0)]
    [InlineData("<Border Opacity=\"{x:Bind RowOpacity(Row.IsCompleted, Row.KeepsFullStrength), Mode=OneWay}\" />", 1)]
    [InlineData("<Border Opacity=\"{x:Bind Pick(x:True, 'a', -1.5, Row.Flag), Mode=OneWay}\" />", 1)]
    [InlineData("<Border Opacity=\"{x:Bind Pick(x:True, 'a, b', -1.5, x:Null), Mode=OneTime}\" />", 0)]
    [InlineData("<Border Style=\"{x:Bind Path=DueChipStyle(Row.DueState), Mode=OneWay}\" />", 1)]
    [InlineData("<Border Tag=\"{x:Bind Now(), Mode=OneTime}\" />", 0)]
    [InlineData("<TextBlock Text=\"{x:Bind ViewModel.Title, Mode=OneWay}\" />", 0)]
    [InlineData("<TextBlock Text=\"{x:Bind ViewModel.Title, Mode=OneWay, TargetNullValue=''}\" />", 0)]
    [InlineData("<TextBlock\n  Text=\"{x:Bind Hash(Name),\n    Mode=OneTime}\" Tag=\"{x:Bind Hash(Other), Mode=OneTime}\" />", 2)]
    public void Function_scanner_flags_a_path_argument_without_a_FallbackValue(string xaml, int expected)
    {
        Assert.Equal(expected, ScanFunctionBindings(xaml).Count());
    }

    [Fact]
    public void Function_scanner_reports_the_line_of_the_binding()
    {
        var xaml = "<Grid>\n  <Ellipse />\n  <Ellipse Fill=\"{x:Bind Brush(ColorHex), Mode=OneWay}\" />\n</Grid>";

        Assert.Equal(["3: {x:Bind Brush(ColorHex), Mode=OneWay}"], ScanFunctionBindings(xaml));
    }

    // Each x:Bind whose path is a function call with at least one property-path argument (anything but a literal:
    // x:True, x:False, x:Null, a number or a quoted string) and no FallbackValue. A source-text check cannot tell a
    // value-type argument from a nullable one, so every path argument counts.
    private static IEnumerable<string> ScanFunctionBindings(string text)
    {
        foreach (Match start in XBindStart().Matches(text))
        {
            var binding = MarkupExtensionAt(text, start.Index);
            var call = FunctionPath().Match(binding);
            if (!call.Success || FallbackValueSetting().IsMatch(binding))
            {
                continue;
            }

            if (SplitArguments(call.Groups["args"].Value).Any(a => !Literal().IsMatch(a)))
            {
                var line = text.AsSpan(0, start.Index).Count('\n') + 1;
                yield return $"{line}: {Regex.Replace(binding, "\\s+", " ")}";
            }
        }
    }

    // The markup extension that opens at `index`, through its matching closing brace.
    private static string MarkupExtensionAt(string text, int index)
    {
        var depth = 0;
        for (var i = index; i < text.Length; i++)
        {
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return text[index..(i + 1)];
            }
        }

        return text[index..];
    }

    // Arguments split on commas outside quotes, trimmed; none for an empty list.
    private static IEnumerable<string> SplitArguments(string args)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in args)
        {
            if (c == '\'')
            {
                quoted = !quoted;
            }

            if (c == ',' && !quoted)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString().Trim());
        return parts.Where(p => p.Length > 0);
    }

    [GeneratedRegex("\\{\\s*x:Bind\\b")]
    private static partial Regex XBindStart();

    [GeneratedRegex("^\\{\\s*x:Bind\\s+(?:Path\\s*=\\s*)?[\\w.:]+\\s*\\((?<args>[^)]*)\\)")]
    private static partial Regex FunctionPath();

    [GeneratedRegex("[\\s,]FallbackValue\\s*=")]
    private static partial Regex FallbackValueSetting();

    [GeneratedRegex("^(?:x:True|x:False|x:Null|-?\\d+(?:\\.\\d+)?|'[^']*')$")]
    private static partial Regex Literal();

    private static IEnumerable<string> Scan(string text)
    {
        foreach (var regex in new[] { RuntimeBinding(), DynamicResourceReference() })
        {
            foreach (Match m in regex.Matches(text))
            {
                var line = text.AsSpan(0, m.Index).Count('\n') + 1;
                yield return $"{line}: {m.Value.Trim()}";
            }
        }
    }

    // XAML files of every project under root whose project file sets UseWinUI, minus build output.
    private static IEnumerable<string> WinUiXamlFiles(string root)
    {
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => UseWinUiProperty().IsMatch(File.ReadAllText(p)));

        foreach (var project in projects)
        {
            var dir = Path.GetDirectoryName(project)!;
            foreach (var file in Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(dir, file)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!parts.Contains("obj") && !parts.Contains("bin"))
                {
                    yield return file;
                }
            }
        }
    }

    private static void AddProject(string root, string name, string property, string xamlPath, string xaml)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), $"<Project><PropertyGroup>{property}</PropertyGroup></Project>");
        var xamlFile = Path.Combine(dir, xamlPath);
        Directory.CreateDirectory(Path.GetDirectoryName(xamlFile)!);
        File.WriteAllText(xamlFile, xaml);
    }

    private static string FindRepoRoot() =>
        typeof(WinUiXamlGuardrailTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepoRoot").Value
        ?? throw new InvalidOperationException("The RepoRoot assembly metadata has no value.");
}
