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
        var migrationRunner = new SqliteMigrationRunner(_options, backupService,
        [
            new SqliteMigration(1, SchemaSql),
            new SqliteMigration(2, ServiceTokenAuditMigrationSql),
            new SqliteMigration(3, StateFoundationMigrationSql),
            new SqliteMigration(4, WorkItemSearchMigrationSql),
        ]);
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

    private const string ServiceTokenAuditMigrationSql = """
        CREATE TABLE events_v2 (
            id TEXT PRIMARY KEY, project_id TEXT NULL, entity_type TEXT NOT NULL,
            entity_id TEXT NOT NULL, event_type TEXT NOT NULL, actor_type TEXT NOT NULL,
            actor_name TEXT NOT NULL, before_json TEXT NULL, after_json TEXT NULL,
            message TEXT NULL, created_at TEXT NOT NULL,
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        INSERT INTO events_v2 SELECT * FROM events;
        DROP TABLE events;
        ALTER TABLE events_v2 RENAME TO events;
        CREATE UNIQUE INDEX service_tokens_audience_unique ON service_tokens(audience);
        """;

    private const string StateFoundationMigrationSql = """
        CREATE TABLE work_item_relations (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, source_work_item_id TEXT NOT NULL,
            target_work_item_id TEXT NOT NULL, relation TEXT NOT NULL,
            created_at TEXT NOT NULL,
            CHECK (source_work_item_id <> target_work_item_id),
            CHECK (relation IN ('relates_to','depends_on','blocks','duplicates','caused_by','implements','supersedes')),
            UNIQUE (source_work_item_id, target_work_item_id, relation),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (source_work_item_id) REFERENCES work_items(id),
            FOREIGN KEY (target_work_item_id) REFERENCES work_items(id)
        );
        CREATE INDEX work_item_relations_project_idx ON work_item_relations(project_id);
        CREATE INDEX work_item_relations_target_idx ON work_item_relations(target_work_item_id);

        CREATE TABLE work_item_comments (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, work_item_id TEXT NOT NULL,
            body TEXT NOT NULL, author_type TEXT NOT NULL, author_name TEXT NOT NULL,
            created_at TEXT NOT NULL,
            CHECK (length(trim(body)) > 0),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (work_item_id) REFERENCES work_items(id)
        );
        CREATE INDEX work_item_comments_item_idx ON work_item_comments(work_item_id, created_at);

        CREATE TABLE work_item_task_links (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, work_item_id TEXT NOT NULL,
            task_system TEXT NOT NULL, task_id TEXT NOT NULL, relation TEXT NOT NULL,
            created_at TEXT NOT NULL,
            UNIQUE (work_item_id, task_system, task_id, relation),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (work_item_id) REFERENCES work_items(id)
        );
        CREATE INDEX work_item_task_links_item_idx ON work_item_task_links(work_item_id);

        CREATE TABLE work_item_commits (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, work_item_id TEXT NOT NULL,
            commit_hash TEXT NOT NULL, relation TEXT NOT NULL, created_at TEXT NOT NULL,
            CHECK (relation IN ('implements','fixes','relates_to')),
            UNIQUE (work_item_id, commit_hash, relation),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (work_item_id) REFERENCES work_items(id)
        );
        CREATE INDEX work_item_commits_item_idx ON work_item_commits(work_item_id);

        CREATE TABLE releases (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, version TEXT NOT NULL,
            channel TEXT NOT NULL, status TEXT NOT NULL, tag_name TEXT NULL,
            commit_hash TEXT NULL, release_notes TEXT NULL, planned_at TEXT NULL,
            released_at TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            deleted_at TEXT NULL, revision INTEGER NOT NULL DEFAULT 1,
            CHECK (channel IN ('alpha','beta','rc','stable','nightly')),
            CHECK (status IN ('draft','planned','preparing','ready','publishing','released','failed','withdrawn')),
            UNIQUE (project_id, version),
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        CREATE INDEX releases_project_status_idx ON releases(project_id, status, channel, released_at);

        CREATE TABLE release_work_items (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, release_id TEXT NOT NULL,
            work_item_id TEXT NOT NULL, relation TEXT NOT NULL, created_at TEXT NOT NULL,
            CHECK (relation IN ('includes','fixes','implements','resolves')),
            UNIQUE (release_id, work_item_id, relation),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (release_id) REFERENCES releases(id),
            FOREIGN KEY (work_item_id) REFERENCES work_items(id)
        );

        CREATE TABLE builds (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, provider TEXT NOT NULL,
            status TEXT NOT NULL, source_commit TEXT NOT NULL, configuration TEXT NOT NULL,
            config_json TEXT NULL, started_at TEXT NULL, finished_at TEXT NULL,
            actor_type TEXT NOT NULL, actor_name TEXT NOT NULL, error_code TEXT NULL,
            error_message TEXT NULL, created_at TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1,
            CHECK (status IN ('queued','preparing','building','succeeded','failed','cancelled')),
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );
        CREATE INDEX builds_project_created_idx ON builds(project_id, created_at);

        CREATE TABLE build_artifacts (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, build_id TEXT NOT NULL,
            name TEXT NOT NULL, artifact_type TEXT NOT NULL, artifact_kind TEXT NOT NULL,
            file_path TEXT NOT NULL, file_size INTEGER NULL, sha256 TEXT NULL,
            manifest_sha256 TEXT NULL, created_at TEXT NOT NULL,
            CHECK (artifact_kind IN ('file','directory')),
            CHECK ((artifact_kind = 'file' AND sha256 IS NOT NULL AND manifest_sha256 IS NULL)
                OR (artifact_kind = 'directory' AND manifest_sha256 IS NOT NULL AND sha256 IS NULL)),
            UNIQUE (build_id, name),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (build_id) REFERENCES builds(id)
        );

        CREATE TABLE release_artifacts (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, release_id TEXT NOT NULL,
            build_artifact_id TEXT NULL, name TEXT NOT NULL, artifact_type TEXT NOT NULL,
            platform TEXT NOT NULL, architecture TEXT NOT NULL, file_name TEXT NOT NULL,
            file_path TEXT NULL, download_url TEXT NULL, file_size INTEGER NULL,
            sha256 TEXT NULL, signature_path TEXT NULL, signature_url TEXT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            CHECK (artifact_type IN ('installer','portable','archive','package','symbols','source','update','documentation','other')),
            CHECK (platform IN ('windows','macos','linux','android','ios','any')),
            CHECK (architecture IN ('x64','arm64','x86','universal','any')),
            UNIQUE (release_id, name),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (release_id) REFERENCES releases(id),
            FOREIGN KEY (build_artifact_id) REFERENCES build_artifacts(id)
        );

        CREATE TABLE deployment_targets (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL UNIQUE, name TEXT NOT NULL,
            mode TEXT NOT NULL, destination_path TEXT NOT NULL, kelpie_target TEXT NULL,
            config_json TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            revision INTEGER NOT NULL DEFAULT 1,
            CHECK (mode IN ('local','server')),
            CHECK ((mode = 'local' AND kelpie_target IS NULL)
                OR (mode = 'server' AND kelpie_target IS NOT NULL)),
            FOREIGN KEY (project_id) REFERENCES projects(id)
        );

        CREATE TABLE deployments (
            id TEXT PRIMARY KEY, project_id TEXT NOT NULL, deployment_target_id TEXT NOT NULL,
            build_id TEXT NOT NULL, release_id TEXT NULL, mode TEXT NOT NULL,
            status TEXT NOT NULL, source_commit TEXT NOT NULL, destination_path TEXT NOT NULL,
            kelpie_target TEXT NULL, previous_deployment_id TEXT NULL,
            rollback_of_deployment_id TEXT NULL, started_at TEXT NULL, finished_at TEXT NULL,
            actor_type TEXT NOT NULL, actor_name TEXT NOT NULL, error_code TEXT NULL,
            error_message TEXT NULL, created_at TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1,
            CHECK (mode IN ('local','server')),
            CHECK (status IN ('pending','preparing','deploying','verifying','succeeded','failed','rolling_back','rolled_back','rollback_failed')),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (deployment_target_id) REFERENCES deployment_targets(id),
            FOREIGN KEY (build_id) REFERENCES builds(id),
            FOREIGN KEY (release_id) REFERENCES releases(id),
            FOREIGN KEY (previous_deployment_id) REFERENCES deployments(id),
            FOREIGN KEY (rollback_of_deployment_id) REFERENCES deployments(id)
        );
        CREATE INDEX deployments_project_created_idx ON deployments(project_id, created_at);

        CREATE TRIGGER events_prevent_update BEFORE UPDATE ON events
        BEGIN SELECT RAISE(ABORT, 'events are append-only'); END;
        CREATE TRIGGER events_prevent_delete BEFORE DELETE ON events
        BEGIN SELECT RAISE(ABORT, 'events are append-only'); END;
        CREATE TRIGGER work_item_comments_prevent_update BEFORE UPDATE ON work_item_comments
        BEGIN SELECT RAISE(ABORT, 'comments are append-only'); END;
        CREATE TRIGGER work_item_comments_prevent_delete BEFORE DELETE ON work_item_comments
        BEGIN SELECT RAISE(ABORT, 'comments are append-only'); END;
        CREATE TRIGGER build_artifacts_prevent_update BEFORE UPDATE ON build_artifacts
        BEGIN SELECT RAISE(ABORT, 'build artifacts are immutable'); END;
        CREATE TRIGGER build_artifacts_prevent_delete BEFORE DELETE ON build_artifacts
        BEGIN SELECT RAISE(ABORT, 'build artifacts are immutable'); END;
        """;

    private const string WorkItemSearchMigrationSql = """
        CREATE VIRTUAL TABLE work_item_search USING fts5(
            project_id UNINDEXED,
            work_item_id UNINDEXED,
            title,
            description,
            comments,
            tokenize='unicode61'
        );
        INSERT INTO work_item_search(project_id,work_item_id,title,description,comments)
        SELECT item.project_id,item.id,item.title,COALESCE(item.description,''),
            COALESCE((SELECT group_concat(comment.body, char(10)) FROM work_item_comments comment WHERE comment.work_item_id=item.id),'')
        FROM work_items item;

        CREATE TRIGGER work_item_search_after_insert AFTER INSERT ON work_items
        BEGIN
            INSERT INTO work_item_search(project_id,work_item_id,title,description,comments)
            VALUES(new.project_id,new.id,new.title,COALESCE(new.description,''),'');
        END;
        CREATE TRIGGER work_item_search_after_update AFTER UPDATE OF title,description ON work_items
        BEGIN
            UPDATE work_item_search SET title=new.title,description=COALESCE(new.description,'') WHERE work_item_id=new.id;
        END;
        CREATE TRIGGER work_item_search_after_delete AFTER DELETE ON work_items
        BEGIN
            DELETE FROM work_item_search WHERE work_item_id=old.id;
        END;
        CREATE TRIGGER work_item_search_comment_after_insert AFTER INSERT ON work_item_comments
        BEGIN
            UPDATE work_item_search SET comments=(
                SELECT COALESCE(group_concat(body, char(10)),'') FROM work_item_comments WHERE work_item_id=new.work_item_id
            ) WHERE work_item_id=new.work_item_id;
        END;
        """;
}
