namespace TaskDesk.Data.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task First_run_creates_the_database_file_and_seeds_one_Default_List_named_Inbox()
    {
        using var db = new TempDatabase();

        await db.Get<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(db.Paths.DatabaseFile));
        var lists = await db.QueryAsync(c => c.Lists);
        var list = Assert.Single(lists);
        Assert.Equal("Inbox", list.Name);
        Assert.True(list.IsDefault);
    }

    [Fact]
    public async Task Initializing_again_does_not_seed_a_second_list()
    {
        using var db = new TempDatabase();
        var initializer = db.Get<IDatabaseInitializer>();

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Single(await db.QueryAsync(c => c.Lists));
    }

    [Fact]
    public async Task A_renamed_Default_List_is_kept_and_not_seeded_again()
    {
        using var db = new TempDatabase();
        var initializer = db.Get<IDatabaseInitializer>();
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        await db.RenameDefaultListAsync("Work");

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        var list = Assert.Single(await db.QueryAsync(c => c.Lists));
        Assert.Equal("Work", list.Name);
    }
}
