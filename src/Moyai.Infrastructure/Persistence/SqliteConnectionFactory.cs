using Microsoft.Data.Sqlite;

namespace Moyai.Infrastructure.Persistence;

/// <summary>必須PRAGMAを適用したSQLite接続を生成します。</summary>
internal static class SqliteConnectionFactory
{
    public static async Task<SqliteConnection> OpenAsync(SqliteDatabaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {options.BusyTimeoutMilliseconds};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
