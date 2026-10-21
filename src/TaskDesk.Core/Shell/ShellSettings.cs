using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskDesk.Core.Shell;

/// <summary>The settings stored in settings.json (never in the database).</summary>
public sealed record ShellSettings(
    bool StartWithWindows = true,
    bool TrayHintShown = false,
    ThemeChoice Theme = ThemeChoice.System,
    CloseBehavior CloseBehavior = CloseBehavior.HideToTray,
    TimeOnly DefaultReminderTime = default,
    WindowLayout? Layout = null)
{
    public static readonly TimeOnly InitialReminderTime = new(9, 0);

    /// <summary>Time used for date-only Reminders; 09:00 unless the owner changed it.</summary>
    public TimeOnly DefaultReminderTime { get; init; } =
        DefaultReminderTime == default ? InitialReminderTime : DefaultReminderTime;

    /// <summary>The global quick-add shortcut; null means the owner disabled it.</summary>
    public Hotkey? Hotkey { get; init; } = Hotkey.Default;
}

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
    private const string ThemeKey = "themeMode";
    private const string CloseKey = "closeBehavior";
    private const string ReminderKey = "defaultReminderTime";
    private const string LayoutKey = "windowLayout";
    private const string HotkeyKey = "globalHotkey";

    public ShellSettings Load()
    {
        var root = ReadRoot();
        return new ShellSettings(
            ReadBool(root[StartKey], true),
            ReadBool(root[HintKey], false),
            ReadEnum(root[ThemeKey], ThemeChoice.System),
            ReadEnum(root[CloseKey], CloseBehavior.HideToTray),
            ReadTime(root[ReminderKey]),
            ReadLayout(root[LayoutKey]))
        {
            Hotkey = ReadHotkey(root[HotkeyKey]),
        };
    }

    public void Save(ShellSettings settings)
    {
        var root = ReadRoot();
        root[StartKey] = settings.StartWithWindows;
        root[HintKey] = settings.TrayHintShown;
        root[ThemeKey] = settings.Theme.ToString();
        root[CloseKey] = settings.CloseBehavior.ToString();
        root[ReminderKey] = settings.DefaultReminderTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        root[LayoutKey] = WriteLayout(settings.Layout);
        root[HotkeyKey] = settings.Hotkey?.ToString() ?? "";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString());
        File.Move(temp, path, overwrite: true);
    }

    // A missing key or a value of the wrong type means the default.
    private static bool ReadBool(JsonNode? node, bool fallback) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static T ReadEnum<T>(JsonNode? node, T fallback) where T : struct, Enum =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
        && Enum.TryParse<T>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    // Missing or unreadable means the default; an empty string means the owner disabled it.
    private static Hotkey? ReadHotkey(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Length == 0 ? null : Hotkey.TryParse(text) ?? Hotkey.Default
            : Hotkey.Default;

    private static TimeOnly ReadTime(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
        && TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : ShellSettings.InitialReminderTime;

    private static JsonNode? WriteLayout(WindowLayout? layout)
    {
        if (layout is null)
        {
            return null;
        }

        var panes = new JsonObject();
        foreach (var (name, width) in layout.PaneWidths ?? new Dictionary<string, double>())
        {
            panes[name] = width;
        }

        return new JsonObject
        {
            ["left"] = layout.Left,
            ["top"] = layout.Top,
            ["width"] = layout.Width,
            ["height"] = layout.Height,
            ["maximized"] = layout.Maximized,
            ["paneWidths"] = panes,
        };
    }

    private static WindowLayout? ReadLayout(JsonNode? node)
    {
        if (node is not JsonObject obj
            || Number(obj["left"]) is not { } left || Number(obj["top"]) is not { } top
            || Number(obj["width"]) is not { } width || Number(obj["height"]) is not { } height)
        {
            return null;
        }

        var panes = new Dictionary<string, double>();
        if (obj["paneWidths"] is JsonObject paneObj)
        {
            foreach (var (name, value) in paneObj)
            {
                if (Number(value) is { } w)
                {
                    panes[name] = w;
                }
            }
        }

        var maximized = obj["maximized"] is JsonValue m && m.TryGetValue<bool>(out var flag) && flag;
        return new WindowLayout(left, top, width, height, maximized, panes);
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number)
            ? number
            : null;

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
