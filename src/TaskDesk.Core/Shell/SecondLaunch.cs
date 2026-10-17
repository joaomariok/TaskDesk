using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TaskDesk.Core.Reminders;

namespace TaskDesk.Core.Shell;

/// <summary>What a later launch asked the running instance to do (SPEC 7.1).</summary>
/// <param name="Minimized">Started with <c>--minimized</c>: do not show the main window.</param>
/// <param name="Activation">The Reminder notification click that launched it, or null.</param>
public sealed record SecondLaunch(bool Minimized, NotificationActivation? Activation);

/// <summary>
/// The arguments a later launch forwards to the running instance: the <c>--minimized</c> flag and the Reminder
/// notification activation, as <c>--reminder:key=value</c> entries read with <see cref="ReminderArguments.Parse"/>.
/// </summary>
public static class SecondLaunchArguments
{
    public const string MinimizedFlag = "--minimized";
    private const string ReminderPrefix = "--reminder:";

    public static IReadOnlyList<string> Build(bool minimized, NotificationActivation? activation)
    {
        var arguments = new List<string>();
        if (minimized)
        {
            arguments.Add(MinimizedFlag);
        }

        if (activation is not null)
        {
            arguments.AddRange(ReminderArguments.For(activation).Select(kv => $"{ReminderPrefix}{kv.Key}={kv.Value}"));
        }

        return arguments;
    }

    /// <summary>Reads forwarded arguments; anything unknown or malformed is ignored.</summary>
    public static SecondLaunch Parse(IEnumerable<string> arguments)
    {
        var minimized = false;
        var reminder = new List<KeyValuePair<string, string>>();
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, MinimizedFlag, StringComparison.OrdinalIgnoreCase))
            {
                minimized = true;
            }
            else if (argument.StartsWith(ReminderPrefix, StringComparison.Ordinal)
                && argument[ReminderPrefix.Length..].Split('=', 2) is [var key, var value])
            {
                reminder.Add(new(key, value));
            }
        }

        return new SecondLaunch(minimized, reminder.Count == 0 ? null : ReminderArguments.Parse(reminder));
    }
}

/// <summary>
/// The wire format of the single-instance pipe: a 4-byte little-endian length, then the arguments as a UTF-8
/// JSON array of strings. A malformed message reads as no arguments (a plain "show the window").
/// </summary>
public static class SecondLaunchMessage
{
    /// <summary>Longer payloads are refused, so a stray client cannot make the listener allocate much.</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    public static async Task WriteAsync(Stream stream, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentException("Too many arguments to forward.", nameof(arguments));
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<string>> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellationToken);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 0 or > MaxPayloadBytes)
            {
                return [];
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            return JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(payload)) is { } arguments
                && arguments.All(a => a is not null)
                    ? arguments
                    : [];
        }
        catch (Exception ex) when (ex is EndOfStreamException or JsonException)
        {
            return [];
        }
    }
}
