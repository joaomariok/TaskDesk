using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskDesk.Data;

/// <summary>Lets <c>dotnet ef migrations add</c> create the context; never used at run time.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TaskDeskDbContext>
{
    public TaskDeskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TaskDeskDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new TaskDeskDbContext(options);
    }
}
