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

    // Uninstalling runs this exe with the hook argument; without the hook the Run entry and the notification
    // registration stay behind (#90).
    [Fact]
    public void App_entry_point_runs_the_uninstall_cleanup_in_Velopack_s_uninstall_hook()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "Program.cs"));
        var main = MainFirstStatement().Match(program);

        Assert.True(main.Success, "Program.cs has no Main method.");
        Assert.Contains(
            ".OnBeforeUninstallFastCallback(VelopackInstall.OnBeforeUninstall)",
            main.Groups["first"].Value,
            StringComparison.Ordinal);
    }

    // Velopack's own apply-on-start runs in every process, before the single-instance check, so a second launch would
    // stop the running copy without its exit path. It is always off; the App applies a downloaded update itself,
    // only when the runtime mode manages updates (#92, review H1).
    [Fact]
    public void App_entry_point_turns_off_Velopack_s_apply_on_start()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "Program.cs"));
        var main = MainFirstStatement().Match(program);

        Assert.True(main.Success, "Program.cs has no Main method.");
        Assert.Contains(".SetAutoApplyOnStartup(false)", main.Groups["first"].Value, StringComparison.Ordinal);
    }

    // Only the single-instance owner applies, and before the host exists: nothing to save yet, no window to lose.
    [Fact]
    public void App_applies_a_downloaded_update_after_the_single_instance_check_and_before_the_host()
    {
        var app = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "App.xaml.cs"));
        var acquire = app.IndexOf("singleInstance.TryAcquire()", StringComparison.Ordinal);
        var apply = app.IndexOf("StartupUpdate.ApplyIfPending(", StringComparison.Ordinal);
        var host = app.IndexOf("Host.CreateApplicationBuilder()", StringComparison.Ordinal);

        Assert.True(acquire >= 0, "App.xaml.cs has no single-instance check.");
        Assert.True(apply > acquire, "StartupUpdate.ApplyIfPending is not called after the single-instance check.");
        Assert.True(apply < host, "StartupUpdate.ApplyIfPending is not called before the host is built.");
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

    private const string ReleaseWorkflow = ".github/workflows/release.yml";

    private static string ReadWorkflow() => File.ReadAllText(Path.Combine(FindRepoRoot(), ReleaseWorkflow));

    // The workflow's steps are the "- name:" blocks of its single job, in file order.
    private static string[] StepNames() =>
        Regex.Matches(ReadWorkflow(), @"^      - name: (?<name>.+)$", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value.Trim()).ToArray();

    [Fact]
    public void Release_workflow_runs_only_for_version_tags_and_may_write_contents()
    {
        var workflow = ReadWorkflow();

        Assert.Matches(@"(?s)\non:\s+push:\s+tags:\s+- 'v\*'\s*\n\s*\npermissions:\s+contents: write\n", workflow);
        Assert.DoesNotContain("branches:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_runs_the_gate_before_it_packs_and_uploads()
    {
        var steps = StepNames();

        // A failing step ends the job, so nothing after the gate runs when the gate fails.
        Assert.Contains("Run gate", steps);
        Assert.True(steps.ToList().IndexOf("Run gate") < steps.ToList().IndexOf("Pack"));
        Assert.True(steps.ToList().IndexOf("Pack") < steps.ToList().IndexOf("Upload the GitHub Release"));
        Assert.Contains("pwsh scripts/gate.ps1", ReadWorkflow(), StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", ReadWorkflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_packs_the_tag_version_with_the_pack_script_and_a_delta_source()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("-Version ${{ steps.version.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.Contains("-DownloadFrom https://github.com/${{ github.repository }}", workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("vpk -- pack", workflow, StringComparison.Ordinal);
        // The pack script, not the workflow, must download the previous release before it packs.
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), PackScript));
        Assert.True(script.IndexOf("'download', 'github'", StringComparison.Ordinal)
            < script.IndexOf("'pack'", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_workflow_version_pattern_is_accepted_by_the_pack_script()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), PackScript));
        var workflow = ReadWorkflow();

        Assert.Contains(@"'^v(?<version>\d+\.\d+\.\d+)$'", workflow, StringComparison.Ordinal);
        Assert.Contains(@"ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_uploads_a_published_release_with_the_workflow_token_and_the_tag()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("vpk -- upload github", workflow, StringComparison.Ordinal);
        Assert.Contains("--token $env:GITHUB_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}", workflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_TAG: ${{ github.ref_name }}", workflow, StringComparison.Ordinal);
        Assert.Contains("--tag $env:RELEASE_TAG", workflow, StringComparison.Ordinal);
        // The tag is a pushed string: pasted into a script's text, a quote in it would run as code.
        Assert.Single(Regex.Matches(workflow, Regex.Escape("${{ github.ref_name }}")));
        Assert.Contains("--publish", workflow, StringComparison.Ordinal);
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
