using Microsoft.Data.Sqlite;
using Moyai.Application.Persistence;

namespace Moyai.Infrastructure.Persistence;

/// <summary>SQLiteの必須設定とv1初期スキーマを適用します。</summary>
public sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private readonly SqliteDatabaseOptions _options;

    /// <summary>初期化サービスを生成します。</summary>
    public SqliteDatabaseInitializer(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        if (options.BusyTimeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, $"PRAGMA busy_timeout = {_options.BusyTimeoutMilliseconds};", cancellationToken).ConfigureAwait(false);
        await connection.CloseAsync().ConfigureAwait(false);
        var backupService = new SqliteBackupService(_options, TimeProvider.System);
        var migrationRunner = new SqliteMigrationRunner(_options, backupService, [new SqliteMigration(1, SchemaSql)]);
        await migrationRunner.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
        INSERT INTO schema_version(version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
        CREATE TABLE IF NOT EXISTS projects (
            id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE,
            description TEXT NULL, source_path TEXT NOT NULL, install_path TEXT NULL,
            repository_url TEXT NOT NULL, repository_provider TEXT NOT NULL,
            build_provider TEXT NOT NULL, build_config_json TEXT NULL,
            deploy_mode TEXT NOT NULL, git_user_name TEXT NULL, git_user_email TEXT NULL,
            git_remote_name TEXT NOT NULL DEFAULT 'origin', git_default_branch TEXT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL, archived_at TEXT NULL,
            revision INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE IF NOT EXISTS work_item_sequences (
            project_id TEXT NOT NULL, type TEXT NOT NULL, next_sequence_no INTEGER NOT NULL,
            PRIMARY KEY (project_id, type), FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        CREATE TABLE IF NOT EXISTS work_items (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, key TEXT NOT NULL,
            sequence_no INTEGER NOT NULL, type TEXT NOT NULL, title TEXT NOT NULL,
            description TEXT NULL, status TEXT NOT NULL, priority TEXT NOT NULL,
            severity TEXT NULL, owner TEXT NULL, metadata_json TEXT NULL,
            created_by_type TEXT NOT NULL, created_by_name TEXT NOT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL, closed_at TEXT NULL,
            deleted_at TEXT NULL, revision INTEGER NOT NULL DEFAULT 1,
            UNIQUE (project_id, key), UNIQUE (project_id, type, sequence_no),
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        CREATE TABLE IF NOT EXISTS events (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, entity_type TEXT NOT NULL,
            entity_id TEXT NOT NULL, event_type TEXT NOT NULL, actor_type TEXT NOT NULL,
            actor_name TEXT NOT NULL, before_json TEXT NULL, after_json TEXT NULL,
            message TEXT NULL, created_at TEXT NOT NULL,
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        CREATE TABLE IF NOT EXISTS service_tokens (
            id TEXT PRIMARY KEY,
            token TEXT NOT NULL UNIQUE,
            audience TEXT NOT NULL,
            scopes_json TEXT NOT NULL,
            issued_at TEXT NOT NULL,
            expires_at TEXT NULL,
            last_used_at TEXT NULL
        );
        """;
}
