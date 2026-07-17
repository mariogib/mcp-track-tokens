using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// Ensures SQLite foreign key enforcement is enabled for every connection.
/// </summary>
public sealed class SqliteForeignKeyInterceptor : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        EnableForeignKeys(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private static void EnableForeignKeys(DbConnection connection)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    private static async Task EnableForeignKeysAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
