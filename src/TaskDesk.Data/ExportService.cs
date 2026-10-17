using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Export;

namespace TaskDesk.Data;

internal sealed class ExportService(IDbContextFactory<TaskDeskDbContext> factory, TimeProvider clock) : IExportService
{
    public async Task ExportJsonAsync(string path, string appVersion, CancellationToken cancellationToken = default)
    {
        var data = await LoadAsync(cancellationToken);
        var bytes = JsonExportFormat.Serialize(data, clock.GetUtcNow().UtcDateTime, appVersion);
        await WriteAtomicallyAsync(path, bytes, cancellationToken);
    }

    public async Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        var data = await LoadAsync(cancellationToken);
        var bytes = CsvExportFormat.Serialize(data, clock.LocalTimeZone);
        await WriteAtomicallyAsync(path, bytes, cancellationToken);
    }

    private async Task<ExportData> LoadAsync(CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // One context, so the lists, tags and tasks all come from the same moment of the database.
        var lists = await context.Lists.AsNoTracking().ToListAsync(cancellationToken);
        var tags = await context.Tags.AsNoTracking().ToListAsync(cancellationToken);
        var tasks = await context.Tasks.AsNoTracking()
            .WithDetails() // the export orders Tags and Subtasks itself
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new ExportData(lists, tags, tasks);
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
