using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Tests;

public class RuntimeModeTests
{
    [Theory]
    [InlineData(@"C:\temp\taskdesk", null, true)]
    [InlineData(@"C:\temp\taskdesk", "", true)]
    [InlineData(@"C:\temp\taskdesk", "1", true)]
    [InlineData(@"C:\temp\taskdesk", "0", false)]
    [InlineData(@"C:\temp\taskdesk", " 0 ", false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData(null, "1", false)]
    public void Resolve_is_sandbox_only_for_a_redirected_data_folder_without_opt_out(
        string? dataDirectory, string? sandboxOverride, bool expected)
    {
        Assert.Equal(expected, RuntimeMode.Resolve(dataDirectory, sandboxOverride).IsSandbox);
    }

    [Fact]
    public void Normal_is_not_sandbox()
    {
        Assert.False(RuntimeMode.Normal.IsSandbox);
    }
}
