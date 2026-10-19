using TaskDesk.Core.Paths;

namespace TaskDesk.Core.Tests;

public class RuntimeModeTests
{
    [Theory]
    [InlineData(@"C:\temp\taskdesk", null, false)]
    [InlineData(@"C:\temp\taskdesk", "0", false)]
    [InlineData(@"C:\temp\taskdesk", " 0 ", false)]
    [InlineData(@"C:\temp\taskdesk", "1", false)]
    [InlineData(null, null, true)]
    [InlineData("", "0", true)]
    [InlineData("   ", null, true)]
    [InlineData(null, "1", true)]
    public void A_redirected_data_folder_never_manages_auto_start(
        string? dataDirectory, string? sandboxOverride, bool expected)
    {
        Assert.Equal(expected, RuntimeMode.Resolve(dataDirectory, sandboxOverride).ManagesAutoStart);
    }

    [Theory]
    [InlineData(@"C:\temp\taskdesk", null, false)]
    [InlineData(@"C:\temp\taskdesk", "", false)]
    [InlineData(@"C:\temp\taskdesk", "1", false)]
    [InlineData(@"C:\temp\taskdesk", "0", true)]
    [InlineData(@"C:\temp\taskdesk", " 0 ", true)]
    [InlineData(null, null, true)]
    [InlineData("", null, true)]
    [InlineData("   ", null, true)]
    [InlineData(null, "1", true)]
    public void Toast_registration_follows_the_sandbox_override_for_a_redirected_data_folder(
        string? dataDirectory, string? sandboxOverride, bool expected)
    {
        Assert.Equal(expected, RuntimeMode.Resolve(dataDirectory, sandboxOverride).RegistersNotifications);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", null, true)]
    [InlineData("   ", "0", true)]
    [InlineData(null, "1", true)]
    [InlineData(@"C:\temp\taskdesk", null, false)]
    [InlineData(@"C:\temp\taskdesk", "1", false)]
    [InlineData(@"C:\temp\taskdesk", "0", false)]
    [InlineData(@"C:\temp\taskdesk", " 0 ", false)]
    public void A_redirected_data_folder_never_manages_updates(
        string? dataDirectory, string? sandboxOverride, bool expected)
    {
        Assert.Equal(expected, RuntimeMode.Resolve(dataDirectory, sandboxOverride).ManagesUpdates);
    }

    [Fact]
    public void Normal_manages_auto_start_registers_notifications_and_manages_updates()
    {
        Assert.True(RuntimeMode.Normal.ManagesAutoStart);
        Assert.True(RuntimeMode.Normal.RegistersNotifications);
        Assert.True(RuntimeMode.Normal.ManagesUpdates);
        Assert.Equal("normal (updates on)", RuntimeMode.Normal.ToString());
    }

    [Fact]
    public void ToString_names_what_is_suppressed()
    {
        Assert.Equal(
            "sandbox (auto-start untouched, notifications not registered, updates off)",
            RuntimeMode.Resolve(@"C:\t", null).ToString());
        Assert.Equal(
            "redirected data folder (auto-start untouched, notifications registered, updates off)",
            RuntimeMode.Resolve(@"C:\t", "0").ToString());
    }
}
