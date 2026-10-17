using TaskDesk.Core.Paths;
using TaskDesk.Data;

// Usage: TaskDesk.SampleData <data-folder> [task-count]
// Writes a database with sample Tasks (no Reminders) into <data-folder>. Refuses the real data folder.
if (args.Length is < 1 or > 2 || (args.Length == 2 && !int.TryParse(args[1], out _)))
{
    Console.Error.WriteLine("Usage: TaskDesk.SampleData <data-folder> [task-count]");
    return 2;
}

var folder = Path.GetFullPath(args[0]);
var real = AppPaths.Resolve(null).DataDirectory;
if (string.Equals(folder.TrimEnd('\\'), real.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Refusing to write sample data into the real TaskDesk data folder.");
    return 2;
}

var count = args.Length == 2 ? int.Parse(args[1]) : 5000;
var paths = AppPaths.ForRoot(folder);
if (File.Exists(paths.DatabaseFile))
{
    Console.Error.WriteLine($"{paths.DatabaseFile} already exists; use an empty folder.");
    return 2;
}

await SampleDataGenerator.GenerateAsync(paths, count);
Console.WriteLine($"Wrote {count} sample Tasks to {paths.DatabaseFile}");
return 0;
