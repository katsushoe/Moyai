using Microsoft.Data.Sqlite;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class ProviderMappingMigrationTests
{
    [Fact]
    public async Task VersionSevenAddsUnsetMappingAndPreservesReferencesAfterBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "moyai-mapping-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "test.db"), Pooling = false }.ToString();
        string id = Guid.NewGuid().ToString("D");
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_version(version INTEGER NOT NULL); INSERT INTO schema_version VALUES(6); CREATE TABLE projects(id TEXT PRIMARY KEY,name TEXT NOT NULL,revision INTEGER NOT NULL); CREATE TABLE linked(project_id TEXT REFERENCES projects(id)); INSERT INTO projects VALUES($id,'Neon_SDK',4); INSERT INTO linked VALUES($id);";
                command.Parameters.AddWithValue("$id", id);
                await command.ExecuteNonQueryAsync();
            }
            var options = new SqliteDatabaseOptions(connectionString, BackupDirectory: Path.Combine(root, "backups"));
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "*.db"));
            await using var upgraded = new SqliteConnection(connectionString);
            await upgraded.OpenAsync();
            await using SqliteCommand check = upgraded.CreateCommand();
            check.CommandText = "SELECT name,revision,provider_repository_id FROM projects WHERE id=$id;";
            check.Parameters.AddWithValue("$id", id);
            await using (SqliteDataReader reader = await check.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("Neon_SDK", reader.GetString(0));
                Assert.Equal(4, reader.GetInt64(1));
                Assert.True(reader.IsDBNull(2));
            }
            check.CommandText = "PRAGMA foreign_key_check;";
            await using (SqliteDataReader reader = await check.ExecuteReaderAsync()) Assert.False(await reader.ReadAsync());
            check.CommandText = "SELECT version FROM schema_version;";
            Assert.Equal(7L, await check.ExecuteScalarAsync());
        }
        finally { Directory.Delete(root, true); }
    }
}
