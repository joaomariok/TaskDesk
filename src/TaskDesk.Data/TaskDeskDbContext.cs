using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

public sealed class TaskDeskDbContext(DbContextOptions<TaskDeskDbContext> options) : DbContext(options)
{
    public DbSet<TaskList> Lists => Set<TaskList>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Instants are stored as UTC and always come back with Kind = Utc (never DateTimeOffset on SQLite).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskList>(list =>
        {
            list.ToTable("Lists");
            list.HasKey(l => l.Id);
            list.Property(l => l.Name).IsRequired().HasMaxLength(100).UseCollation("NOCASE");
            list.HasIndex(l => l.Name).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(task =>
        {
            task.ToTable("Tasks");
            task.HasKey(t => t.Id);
            task.Property(t => t.Title).IsRequired().HasMaxLength(TaskTitle.MaxLength);
            task.HasOne<TaskList>().WithMany().HasForeignKey(t => t.ListId).OnDelete(DeleteBehavior.Cascade);
            task.HasIndex(t => new { t.ListId, t.IsCompleted, t.SortOrder });
        });
    }
}
