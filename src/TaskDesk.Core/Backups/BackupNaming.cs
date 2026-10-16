using System.Globalization;

namespace TaskDesk.Core.Backups;

/// <summary>Backup file naming (<c>taskdesk-YYYYMMDD-HHmmss.db</c>) and the keep-newest-N retention rule.</summary>
public static class BackupNaming
{
    public const int KeepCount = 10;
    private const string Prefix = "taskdesk-";
    private const string Extension = ".db";
    private const string Format = "yyyyMMdd-HHmmss";

    public static string FileName(DateTime localTime) =>
        Prefix + localTime.ToString(Format, CultureInfo.InvariantCulture) + Extension;

    /// <summary>Reads the timestamp out of a backup file name; false for any other file.</summary>
    public static bool TryParse(string fileName, out DateTime localTime)
    {
        localTime = default;
        return fileName.StartsWith(Prefix, StringComparison.Ordinal)
               && fileName.EndsWith(Extension, StringComparison.Ordinal)
               && DateTime.TryParseExact(
                   fileName[Prefix.Length..^Extension.Length], Format,
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out localTime);
    }

    /// <summary>The backup names to delete so only the newest <paramref name="keep"/> remain. Other files are ignored.</summary>
    public static IReadOnlyList<string> SelectExpired(IEnumerable<string> fileNames, int keep = KeepCount) =>
        fileNames
            .Select(name => (Name: name, Ok: TryParse(name, out var time), Time: time))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Time)
            .Skip(keep)
            .Select(x => x.Name)
            .ToList();
}
