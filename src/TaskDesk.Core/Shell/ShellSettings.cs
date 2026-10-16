using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskDesk.Core.Shell;

/// <summary>Settings owned by the tray/auto-start feature.</summary>
public sealed record ShellSettings(bool StartWithWindows = true, bool TrayHintShown = false);

public interface IShellSettingsStore
{
    ShellSettings Load();
    void Save(ShellSettings settings);
}

/// <summary>
/// Reads and writes the shell keys of settings.json, leaving every other key untouched.
/// Writes are atomic (temp file, then replace).
/// </summary>
public sealed class JsonShellSettingsStore(string path) : IShellSettingsStore
{
    private const string StartKey = "startWithWindows";
    private const string HintKey = "trayHintShown";

    public ShellSettings Load()
    {
        var root = ReadRoot();
        return new ShellSettings(
            root[StartKey]?.GetValue<bool>() ?? true,
            root[HintKey]?.GetValue<bool>() ?? false);
    }

    public void Save(ShellSettings settings)
    {
        var root = ReadRoot();
        root[StartKey] = settings.StartWithWindows;
        root[HintKey] = settings.TrayHintShown;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString());
        File.Move(temp, path, overwrite: true);
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
