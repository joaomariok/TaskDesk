using Microsoft.EntityFrameworkCore;
using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Data;

public sealed class TaskDeskDbContext(DbContextOptions<TaskDeskDbContext> options) : DbContext(options)
{
    public DbSet<TaskList> Lists => Set<TaskList>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

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
            list.Property(l => l.ColorHex).HasMaxLength(7);
            list.HasIndex(l => l.Name).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(task =>
        {
            task.ToTable("Tasks");
            task.HasKey(t => t.Id);
            task.Property(t => t.Title).IsRequired().HasMaxLength(TaskTitle.MaxLength);
            task.Property(t => t.Notes).HasMaxLength(TaskNotes.MaxLength);
            task.HasOne<TaskList>().WithMany().HasForeignKey(t => t.ListId).OnDelete(DeleteBehavior.Cascade);
            task.HasIndex(t => new { t.ListId, t.IsCompleted, t.SortOrder });
            task.HasIndex(t => t.DueDate);
            task.HasMany(t => t.Tags).WithMany().UsingEntity<TaskTag>(
                join => join.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId).OnDelete(DeleteBehavior.Cascade),
                join => join.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("TaskTags");
                    join.HasKey(l => new { l.TaskId, l.TagId });
                });
        });

        modelBuilder.Entity<Subtask>(subtask =>
        {
            subtask.ToTable("Subtasks");
            subtask.HasKey(s => s.Id);
            subtask.Property(s => s.Title).IsRequired().HasMaxLength(TaskTitle.MaxLength);
            subtask.HasOne<TaskItem>().WithMany(t => t.Subtasks).HasForeignKey(s => s.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            subtask.HasIndex(s => new { s.TaskId, s.SortOrder });
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.ToTable("Tags");
            tag.HasKey(t => t.Id);
            tag.Property(t => t.Name).IsRequired().HasMaxLength(TagName.MaxLength);
            tag.Property(t => t.NormalizedName).IsRequired().HasMaxLength(TagName.MaxLength);
            tag.HasIndex(t => t.NormalizedName).IsUnique();
        });
    }
}
