using Microsoft.Data.Sqlite;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsyncCreatesVersionThreeSchema()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"moyai-{Guid.NewGuid():N}.db");
        try
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString();
            var initializer = new SqliteDatabaseInitializer(new SqliteDatabaseOptions(connectionString));
            await initializer.InitializeAsync(CancellationToken.None);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('projects','work_items','events','service_tokens','work_item_relations','work_item_comments','work_item_task_links','work_item_commits','releases','release_work_items','release_artifacts','builds','build_artifacts','deployment_targets','deployments');";
            object? count = await command.ExecuteScalarAsync(CancellationToken.None);
            Assert.Equal(15L, count);
            command.CommandText = "SELECT version FROM schema_version;";
            Assert.Equal(4L, await command.ExecuteScalarAsync(CancellationToken.None));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task VersionThreeSchemaEnforcesCrossEntityConstraints()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"moyai-{Guid.NewGuid():N}.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            var initializer = new SqliteDatabaseInitializer(new SqliteDatabaseOptions(connectionString));
            await initializer.InitializeAsync(CancellationToken.None);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");
            string projectId = Guid.NewGuid().ToString("D");
            string itemId = Guid.NewGuid().ToString("D");
            string now = DateTimeOffset.UtcNow.ToString("O");
            await ExecuteAsync(connection, $"""
                INSERT INTO projects(id,name,source_path,repository_url,repository_provider,build_provider,deploy_mode,git_remote_name,created_at,updated_at,revision)
                VALUES('{projectId}','schema-test','C:\\src','https://example.test/repo','github','csharp','local','origin','{now}','{now}',1);
                INSERT INTO work_items(id,project_id,key,sequence_no,type,title,status,priority,created_by_type,created_by_name,created_at,updated_at,revision)
                VALUES('{itemId}','{projectId}','BUG-1',1,'bug','test','open','normal','human','test','{now}','{now}',1);
                """);

            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, $"INSERT INTO work_item_comments(id,project_id,work_item_id,body,author_type,author_name,created_at) VALUES('{Guid.NewGuid():D}','{projectId}','{Guid.NewGuid():D}','body','human','test','{now}');"));
            await ExecuteAsync(connection, $"INSERT INTO deployment_targets(id,project_id,name,mode,destination_path,created_at,updated_at,revision) VALUES('{Guid.NewGuid():D}','{projectId}','local','local','C:\\app','{now}','{now}',1);");
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, $"INSERT INTO deployment_targets(id,project_id,name,mode,destination_path,created_at,updated_at,revision) VALUES('{Guid.NewGuid():D}','{projectId}','duplicate','local','C:\\other','{now}','{now}',1);"));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, $"INSERT INTO releases(id,project_id,version,channel,status,created_at,updated_at,revision) VALUES('{Guid.NewGuid():D}','{projectId}','1.0.0','invalid','draft','{now}','{now}',1);"));
            await ExecuteAsync(connection, $"INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,created_at) VALUES('{Guid.NewGuid():D}','{projectId}','project','{projectId}','test_event','system','test','{now}');");
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "UPDATE events SET message='changed';"));
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "DELETE FROM events;"));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsyncUpgradesVersionTwoDatabaseAfterBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "moyai.db");
        string backupDirectory = Path.Combine(root, "backups");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(CancellationToken.None);
                await ExecuteAsync(connection, """
                    CREATE TABLE schema_version(version INTEGER NOT NULL);
                    INSERT INTO schema_version(version) VALUES(2);
                    CREATE TABLE projects(id TEXT PRIMARY KEY);
                    CREATE TABLE work_items(id TEXT PRIMARY KEY, project_id TEXT NOT NULL, title TEXT NOT NULL, description TEXT NULL);
                    CREATE TABLE events(id TEXT PRIMARY KEY);
                    CREATE TABLE service_tokens(id TEXT PRIMARY KEY);
                    """);
            }

            var options = new SqliteDatabaseOptions(connectionString, BackupDirectory: backupDirectory);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);

            Assert.Single(Directory.GetFiles(backupDirectory, "*.db"));
            await using var upgraded = new SqliteConnection(connectionString);
            await upgraded.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = upgraded.CreateCommand();
            command.CommandText = "SELECT version FROM schema_version;";
            Assert.Equal(4L, await command.ExecuteScalarAsync(CancellationToken.None));
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='deployments';";
            Assert.Equal(1L, await command.ExecuteScalarAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
