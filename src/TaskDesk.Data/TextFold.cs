using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaskDesk.Data;

/// <summary>
/// Case folding for search. SQLite's LIKE and lower() fold ASCII letters only, so "ângela" would not find
/// "Ângela". This registers <see cref="SqlName"/> on every connection and maps <see cref="Fold"/> to it, so a
/// query compares folded text on both sides for Task titles, notes and Subtask titles alike.
/// </summary>
internal static class TextFold
{
    public const string SqlName = "taskdesk_fold";

    /// <summary>The folded form of <paramref name="text"/> (invariant lower case); null stays null.</summary>
    public static string? Apply(string? text) => text?.ToLowerInvariant();

    /// <summary>Translated to the SQL function <see cref="SqlName"/>; never runs in memory.</summary>
    public static string? Fold(string? text) =>
        throw new InvalidOperationException($"{nameof(Fold)} runs in SQL only; use {nameof(Apply)} in memory.");

    public static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
        {
            sqlite.CreateFunction<string?, string?>(SqlName, Apply, isDeterministic: true);
        }
    }
}

/// <summary>Registers the TaskDesk SQL functions on each connection EF opens.</summary>
internal sealed class SqliteFunctionsInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        TextFold.Register(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TextFold.Register(connection);
        return Task.CompletedTask;
    }
}
