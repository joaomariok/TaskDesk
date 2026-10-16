using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TaskDesk.Data;

/// <summary>Writes instants as UTC and marks them <see cref="DateTimeKind.Utc"/> on read.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
