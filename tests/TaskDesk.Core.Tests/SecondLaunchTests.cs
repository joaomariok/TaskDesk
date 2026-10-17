using System.Buffers.Binary;
using TaskDesk.Core.Reminders;
using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public sealed class SecondLaunchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid TaskId = Guid.Parse("0199f0a1-1111-7222-8333-444455556666");

    [Fact]
    public async Task A_message_round_trips_its_arguments_through_a_stream()
    {
        using var stream = new MemoryStream();
        string[] arguments = ["--minimized", "--reminder:taskId=x", "Ângela – reunião"];

        await SecondLaunchMessage.WriteAsync(stream, arguments, Ct);
        stream.Position = 0;
        var read = await SecondLaunchMessage.ReadAsync(stream, Ct);

        Assert.Equal(arguments, read);
    }

    [Fact]
    public async Task An_empty_message_round_trips_as_no_arguments()
    {
        using var stream = new MemoryStream();

        await SecondLaunchMessage.WriteAsync(stream, [], Ct);
        stream.Position = 0;

        Assert.Empty(await SecondLaunchMessage.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task A_truncated_message_reads_as_no_arguments()
    {
        using var stream = new MemoryStream();
        await SecondLaunchMessage.WriteAsync(stream, ["--minimized"], Ct);
        stream.SetLength(stream.Length - 2);
        stream.Position = 0;

        Assert.Empty(await SecondLaunchMessage.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task An_oversized_length_reads_as_no_arguments()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, SecondLaunchMessage.MaxPayloadBytes + 1);
        using var stream = new MemoryStream(header);

        Assert.Empty(await SecondLaunchMessage.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task A_payload_that_is_not_a_string_array_reads_as_no_arguments()
    {
        var payload = "{\"not\":\"an array\"}"u8.ToArray();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        using var stream = new MemoryStream([.. header, .. payload]);

        Assert.Empty(await SecondLaunchMessage.ReadAsync(stream, Ct));
    }

    [Theory]
    [InlineData(ReminderAction.Open)]
    [InlineData(ReminderAction.MarkDone)]
    [InlineData(ReminderAction.SnoozeShort)]
    [InlineData(ReminderAction.SnoozeLong)]
    public void A_toast_activation_is_forwarded_and_parsed_back(ReminderAction action)
    {
        var activation = new NotificationActivation(TaskId, action);

        var parsed = SecondLaunchArguments.Parse(SecondLaunchArguments.Build(minimized: true, activation));

        Assert.True(parsed.Minimized);
        Assert.Equal(activation, parsed.Activation);
    }

    [Fact]
    public void The_missed_Reminders_summary_activation_is_forwarded()
    {
        var parsed = SecondLaunchArguments.Parse(
            SecondLaunchArguments.Build(minimized: false, NotificationActivation.OpenToday));

        Assert.False(parsed.Minimized);
        Assert.Equal(NotificationActivation.OpenToday, parsed.Activation);
    }

    [Fact]
    public void A_plain_launch_forwards_nothing()
    {
        var arguments = SecondLaunchArguments.Build(minimized: false, activation: null);

        Assert.Empty(arguments);
        Assert.Equal(new SecondLaunch(false, null), SecondLaunchArguments.Parse(arguments));
    }

    [Fact]
    public void The_minimized_flag_is_matched_ignoring_case()
    {
        Assert.True(SecondLaunchArguments.Parse(["--MINIMIZED"]).Minimized);
    }

    [Fact]
    public void Unknown_or_malformed_arguments_are_ignored()
    {
        var parsed = SecondLaunchArguments.Parse(
            ["--reminder:action=done", "--reminder:taskId=not-a-guid", "--reminder:", "whatever"]);

        Assert.Equal(new SecondLaunch(false, null), parsed);
    }
}
