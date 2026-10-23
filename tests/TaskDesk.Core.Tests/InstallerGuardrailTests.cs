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

    // Only the single-instance owner applies, and before the host exists: nothing to save yet, no window to lose. The
    // launch gate claims the guard before it applies (LaunchGateTests); the App asks the gate before the host (#99).
    [Fact]
    public void App_asks_the_launch_gate_before_it_builds_the_host()
    {
        var app = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "App.xaml.cs"));
        var gate = app.IndexOf("LaunchGate.DecideAsync(", StringComparison.Ordinal);
        var host = app.IndexOf("Host.CreateApplicationBuilder()", StringComparison.Ordinal);

        Assert.True(gate >= 0, "App.xaml.cs does not ask the launch gate.");
        Assert.True(gate < host, "LaunchGate.DecideAsync is not called before the host is built.");
    }

    // A toast launch that reads the activation's data before notifications are registered fail-fasts in the Windows
    // App SDK runtime (#142, #104): the launch gate learns of a toast launch from the command line instead, and the
    // toast's action is read only after registration.
    [Theory]
    [InlineData("ReadLaunchActivation(")]
    [InlineData("GetActivatedEventArgs(")]
    [InlineData("GetLaunchActivation(")]
    public void App_does_not_read_the_toast_s_data_before_the_launch_gate(string read)
    {
        var app = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TaskDesk.App", "App.xaml.cs"));
        var gate = app.IndexOf("LaunchGate.DecideAsync(", StringComparison.Ordinal);
        var first = app.IndexOf(read, StringComparison.Ordinal);

        Assert.True(gate >= 0, "App.xaml.cs does not ask the launch gate.");
        Assert.True(first < 0 || first > gate, $"App.xaml.cs calls {read} before LaunchGate.DecideAsync.");
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

        Assert.Contains("RELEASE_VERSION: ${{ steps.version.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.Contains("-Version $env:RELEASE_VERSION", workflow, StringComparison.Ordinal);
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

    [Fact]
    public void Release_workflow_checks_the_changelog_before_the_gate_the_pack_and_the_upload()
    {
        var steps = StepNames().ToList();
        var workflow = ReadWorkflow();

        Assert.Contains("Check the CHANGELOG", steps);
        Assert.True(steps.IndexOf("Check the CHANGELOG") < steps.IndexOf("Run gate"));
        Assert.True(steps.IndexOf("Check the CHANGELOG") < steps.IndexOf("Pack"));
        Assert.True(steps.IndexOf("Check the CHANGELOG") < steps.IndexOf("Upload the GitHub Release"));
        // It reads the section with the script the pack uses, and throws when there is none.
        Assert.Contains("scripts/get-changelog-section.ps1 -Version $env:RELEASE_VERSION", workflow,
            StringComparison.Ordinal);
        Assert.Matches(@"(?s)get-changelog-section\.ps1.{0,400}throw ", workflow);
    }

    [Fact]
    public void Pack_script_passes_the_changelog_section_to_velopack_as_release_notes()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), PackScript));

        Assert.Contains("get-changelog-section.ps1", script, StringComparison.Ordinal);
        // The notes are an option of vpk pack, added only when the version has a section.
        Assert.Matches(@"'--releaseNotes',\s*\$\w+", script);
        Assert.True(script.IndexOf("--releaseNotes", StringComparison.Ordinal)
            > script.IndexOf("get-changelog-section.ps1", StringComparison.Ordinal));
        Assert.Matches(@"if\s*\(\$notes\)", script);
    }

    [Fact]
    public void Release_workflow_leaves_the_release_body_to_velopack_which_takes_it_from_the_package_notes()
    {
        // Velopack 1.2.161 creates the draft release with the latest package's release notes as its body
        // (GitReleaseUploadCommandRunner, #144), so the upload needs no body of its own.
        var workflow = ReadWorkflow();

        Assert.DoesNotContain("gh release", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--notes", workflow, StringComparison.Ordinal);
    }

    // "## [x.y.z] - YYYY-MM-DD" headings, and the "### " headings under them.
    [GeneratedRegex(@"^## \[(?<version>[^\]]+)\] - (?<date>\d{4}-\d{2}-\d{2})$", RegexOptions.Multiline)]
    private static partial Regex ChangelogReleaseHeading();

    [Fact]
    public void Changelog_has_release_sections_in_the_format_newest_first_with_three_number_versions()
    {
        var changelog = File.ReadAllText(Path.Combine(FindRepoRoot(), "CHANGELOG.md"));
        var all = Regex.Matches(changelog, @"^## .*$", RegexOptions.Multiline).Select(m => m.Value).ToList();
        var releases = ChangelogReleaseHeading().Matches(changelog).ToList();

        // Every "## " heading is a release heading, so there is no Unreleased section.
        Assert.NotEmpty(releases);
        Assert.Equal(all.Count, releases.Count);
        Assert.DoesNotContain("Unreleased", changelog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## [0.1.0] - 2026-10-23", all);
        Assert.DoesNotContain(releases, m => m.Groups["version"].Value is "0.0.1" or "0.0.2");

        var versions = releases.Select(m => m.Groups["version"].Value).ToList();
        Assert.All(versions, v => Assert.Matches(@"^\d+\.\d+\.\d+$", v));
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(versions.OrderByDescending(Version.Parse), versions.Select(Version.Parse).Select(v => v.ToString()));
        var dates = releases.Select(m => DateOnly.ParseExact(m.Groups["date"].Value, "yyyy-MM-dd")).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);

        // Inside a release: Added, Changed and Fixed only, each with at least one bullet.
        var sections = Regex.Split(changelog, @"^## ", RegexOptions.Multiline).Skip(1);
        foreach (var section in sections)
        {
            var kinds = Regex.Matches(section, @"^### (?<kind>.+)$", RegexOptions.Multiline)
                .Select(m => m.Groups["kind"].Value).ToList();
            Assert.NotEmpty(kinds);
            Assert.All(kinds, k => Assert.Contains(k, new[] { "Added", "Changed", "Fixed" }));
            Assert.Matches(@"(?m)^- \S", section);
        }
    }

    [Fact]
    public void Changelog_section_script_prints_one_version_and_nothing_for_an_unknown_one()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "get-changelog-section.ps1"));

        Assert.Contains(@"'^## \[' + [regex]::Escape($Version) + '\] - \d{4}-\d{2}-\d{2}\s*$'", script,
            StringComparison.Ordinal);
        Assert.Contains("if ($line -match '^## ') { break }", script, StringComparison.Ordinal);
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
