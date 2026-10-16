using TaskDesk.Core.Backups;

namespace TaskDesk.Core.Tests;

public sealed class BackupNamingTests
{
    [Fact]
    public void File_name_is_the_local_timestamp()
    {
        Assert.Equal("taskdesk-20261016-093005.db", BackupNaming.FileName(new DateTime(2026, 10, 16, 9, 30, 5)));
    }

    [Theory]
    [InlineData("taskdesk-20261016-093005.db", true)]
    [InlineData("taskdesk-20261016-093005.db.tmp", false)]
    [InlineData("taskdesk-2026-10-16.db", false)]
    [InlineData("other.db", false)]
    public void Only_backup_names_parse(string name, bool expected)
    {
        Assert.Equal(expected, BackupNaming.TryParse(name, out _));
    }

    [Fact]
    public void Parsing_returns_the_timestamp()
    {
        Assert.True(BackupNaming.TryParse("taskdesk-20261016-093005.db", out var time));
        Assert.Equal(new DateTime(2026, 10, 16, 9, 30, 5), time);
    }

    [Fact]
    public void The_oldest_beyond_ten_are_expired_and_other_files_are_ignored()
    {
        var names = Enumerable.Range(1, 12).Select(d => $"taskdesk-202610{d:00}-120000.db").Append("notes.txt").ToList();

        var expired = BackupNaming.SelectExpired(names.OrderByDescending(n => n));

        Assert.Equal(["taskdesk-20261001-120000.db", "taskdesk-20261002-120000.db"], expired.Order());
    }

    [Fact]
    public void Nothing_expires_at_or_below_ten()
    {
        var names = Enumerable.Range(1, 10).Select(d => $"taskdesk-202610{d:00}-120000.db");
        Assert.Empty(BackupNaming.SelectExpired(names));
    }
}
