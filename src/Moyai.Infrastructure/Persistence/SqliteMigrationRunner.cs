using Microsoft.Data.Sqlite;
using Moyai.Application.Persistence;

namespace Moyai.Infrastructure.Persistence;

/// <summary>SQLite MigrationをVersion順に適用します。</summary>
public sealed class SqliteMigrationRunner
{
    private readonly SqliteDatabaseOptions _options;
    private readonly IDatabaseBackupService _backupService;
    private readonly SqliteMigration[] _migrations;

    public SqliteMigrationRunner(SqliteDatabaseOptions options, IDatabaseBackupService backupService, IEnumerable<SqliteMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(migrations);
        _options = options;
        _backupService = backupService;
        _migrations = migrations.OrderBy(static migration => migration.Version).ToArray();
        ValidateMigrations(_migrations);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        int currentVersion = await GetVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        int requiredVersion = _migrations.Length == 0 ? 0 : _migrations[^1].Version;
        if (currentVersion > requiredVersion) throw new InvalidOperationException($"Database version {currentVersion} is newer than supported version {requiredVersion}.");
        SqliteMigration[] pending = _migrations.Where(migration => migration.Version > currentVersion).ToArray();
        if (pending.Length == 0) return;

        if (await HasUserDataAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            await _backupService.CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (SqliteMigration migration in pending)
        {
            await ApplyAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ApplyAsync(SqliteConnection connection, SqliteMigration migration, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version(version) VALUES ($version);";
            versionCommand.Parameters.AddWithValue("$version", migration.Version);
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> GetVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';";
        long exists = (long)(await tableCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        if (exists == 0) return 0;
        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasUserDataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> 'schema_version';";
        long count = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        return count > 0;
    }

    private static void ValidateMigrations(SqliteMigration[] migrations)
    {
        for (int index = 0; index < migrations.Length; index++)
        {
            if (migrations[index].Version != index + 1) throw new ArgumentException("Migration versions must be contiguous and start at 1.", nameof(migrations));
            ArgumentException.ThrowIfNullOrWhiteSpace(migrations[index].Sql);
        }
    }
}
