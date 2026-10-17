using System.Reflection;
using System.Text.RegularExpressions;

namespace TaskDesk.Core.Tests;

/// <summary>
/// Guardrail for the WinUI port (ADR 0004): XAML in a WinUI project (a project file with
/// <c>UseWinUI</c> set to true) uses <c>x:Bind</c> and <c>ThemeResource</c> only, so it stays AOT-ready.
/// WPF XAML is not scanned. Until the WinUI projects exist there is nothing to scan and the test passes.
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
