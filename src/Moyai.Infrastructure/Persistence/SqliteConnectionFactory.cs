using Microsoft.Data.Sqlite;

namespace Moyai.Infrastructure.Persistence;

/// <summary>必須PRAGMAを適用したSQLite接続を生成します。</summary>
internal static class SqliteConnectionFactory
{
    public static async Task<SqliteConnection> OpenAsync(SqliteDatabaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var connection = new SqliteConnection(options.ConnectionString);
        RegisterProjectNameCollation(connection);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {options.BusyTimeoutMilliseconds};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public static void RegisterProjectNameCollation(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.CreateCollation("MOYAI_NOCASE", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
    }
}
