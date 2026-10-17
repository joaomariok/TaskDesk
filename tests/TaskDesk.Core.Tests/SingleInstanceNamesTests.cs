using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public class SingleInstanceNamesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_real_data_folder_keeps_the_shipped_names(string? dataDirectory)
    {
        var names = SingleInstanceNames.For(dataDirectory);

        Assert.Equal(@"Local\TaskDesk.SingleInstance", names.Mutex);
        Assert.Equal("TaskDesk.Activate", names.Pipe);
    }

    [Fact]
    public void A_redirected_data_folder_never_shares_the_real_instance()
    {
        var real = SingleInstanceNames.For(null);
        var redirected = SingleInstanceNames.For(@"C:\temp\taskdesk-a");

        Assert.NotEqual(real.Mutex, redirected.Mutex);
        Assert.NotEqual(real.Pipe, redirected.Pipe);
    }

    [Fact]
    public void Two_redirected_data_folders_run_side_by_side()
    {
        var a = SingleInstanceNames.For(@"C:\temp\taskdesk-a");
        var b = SingleInstanceNames.For(@"C:\temp\taskdesk-b");

        Assert.NotEqual(a.Mutex, b.Mutex);
        Assert.NotEqual(a.Pipe, b.Pipe);
    }

    [Theory]
    [InlineData(@"C:\Temp\TaskDesk-A")]
    [InlineData(@"c:\temp\taskdesk-a\")]
    [InlineData(@"  C:\temp\taskdesk-a  ")]
    [InlineData(@"C:\temp\other\..\taskdesk-a")]
    public void The_same_folder_spelled_differently_is_one_instance(string spelling)
    {
        Assert.Equal(SingleInstanceNames.For(@"C:\temp\taskdesk-a"), SingleInstanceNames.For(spelling));
    }
}
