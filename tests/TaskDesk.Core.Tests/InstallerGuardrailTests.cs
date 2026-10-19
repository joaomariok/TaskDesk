using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Tests;

/// <summary>
/// Guardrail for the Velopack installer (#87): the local pack script packs the App under the package id
/// <c>TaskDesk.App</c>, never the data folder's name, since uninstall deletes <c>%LOCALAPPDATA%\{packId}</c>.
/// </summary>
public sealed partial class InstallerGuardrailTests
{
    private const string PackScript = "scripts/pack-installer.ps1";

    // The script passes vpk's options as a PowerShell array: '--packId', 'TaskDesk.App'.
    [GeneratedRegex(@"'--packId',\s*'(?<value>[^']+)'")]
    private static partial Regex PackIdArgument();

    [Fact]
    public void Pack_script_package_id_is_TaskDesk_App_and_not_the_data_folder_name()
    {
        var packId = Argument(PackIdArgument());
        var dataFolderName = Path.GetFileName(AppPaths.Resolve(null).DataDirectory);

        Assert.Equal("TaskDesk.App", packId);
        Assert.NotEqual(dataFolderName, packId, StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<DefineConstants>[^<]*\bDISABLE_XAML_GENERATED_MAIN\b[^<]*</DefineConstants>")]
    private static partial Regex DisablesGeneratedMain();

    // The first statement of Main, after its opening brace and any comment lines.
    [GeneratedRegex(@"static\s+void\s+Main\s*\([^)]*\)\s*\{(?:\s*//[^\n]*)*\s*(?<first>[^;]+);")]
    private static partial Regex MainFirstStatement();

    [Fact]
    public void App_entry_point_runs_Velopack_before_anything_else()
    {
        var appDir = Path.Combine(FindRepoRoot(), "src", "TaskDesk.App");
        var project = File.ReadAllText(Path.Combine(appDir, "TaskDesk.App.csproj"));
        var program = File.ReadAllText(Path.Combine(appDir, "Program.cs"));

        Assert.Matches(DisablesGeneratedMain(), project);
        var main = MainFirstStatement().Match(program);
        Assert.True(main.Success, "Program.cs has no Main method.");
        Assert.StartsWith("VelopackApp.Build()", main.Groups["first"].Value.Trim(), StringComparison.Ordinal);
        Assert.EndsWith(".Run()", main.Groups["first"].Value.Trim(), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\$OutputDir\s*=\s*Join-Path\s+\$repoRoot\s+'(?<value>[^']+)'")]
    private static partial Regex DefaultOutputDir();

    [Fact]
    public void Pack_script_default_output_folder_is_git_ignored()
    {
        var outputDir = Argument(DefaultOutputDir());
        var ignored = File.ReadAllLines(Path.Combine(FindRepoRoot(), ".gitignore")).Select(line => line.Trim());

        Assert.Contains($"{outputDir}/", ignored);
    }

    [GeneratedRegex(@"'--mainExe',\s*'(?<value>[^']+)'")]
    private static partial Regex MainExeArgument();

    [GeneratedRegex(@"<AssemblyName>(?<value>[^<]+)</AssemblyName>")]
    private static partial Regex AssemblyName();

    [Fact]
    public void Pack_script_main_exe_is_the_App_exe()
    {
        var project = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "TaskDesk.App.csproj"));
        var assemblyName = AssemblyName().Match(project).Groups["value"].Value;

        Assert.Equal("TaskDesk.App.exe", Argument(MainExeArgument()));
        Assert.Equal("TaskDesk.App", assemblyName);
    }

    [GeneratedRegex(@"'--shortcuts',\s*'(?<value>[^']+)'")]
    private static partial Regex ShortcutsArgument();

    [Fact]
    public void Pack_script_makes_a_Start_menu_shortcut_only()
    {
        Assert.Equal("StartMenuRoot", Argument(ShortcutsArgument()));
    }

    [GeneratedRegex(@"<PackageVersion\s+Include=""Velopack""\s+Version=""(?<value>[^""]+)""")]
    private static partial Regex VelopackPackageVersion();

    [Fact]
    public void Velopack_library_and_pinned_vpk_tool_are_both_1_2_161()
    {
        var root = FindRepoRoot();
        var packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        using var tools = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dotnet-tools.json")));
        var vpk = tools.RootElement.GetProperty("tools").GetProperty("vpk").GetProperty("version").GetString();

        Assert.Equal("1.2.161", VelopackPackageVersion().Match(packages).Groups["value"].Value);
        Assert.Equal("1.2.161", vpk);
    }

    [GeneratedRegex(@"'--runtime',\s*'(?<value>[^']+)'")]
    private static partial Regex RuntimeArgument();

    // Without it vpk marks the package x86, while the App is published for win-x64 only.
    [Fact]
    public void Pack_script_packs_for_win_x64()
    {
        Assert.Equal("win-x64", Argument(RuntimeArgument()));
    }

    private static string Argument(Regex pattern)
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), PackScript));
        var match = pattern.Match(script);
        Assert.True(match.Success, $"{PackScript} passes no argument matching {pattern}.");
        return match.Groups["value"].Value;
    }

    private static string FindRepoRoot() =>
        typeof(InstallerGuardrailTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepoRoot").Value
        ?? throw new InvalidOperationException("The RepoRoot assembly metadata has no value.");
}
