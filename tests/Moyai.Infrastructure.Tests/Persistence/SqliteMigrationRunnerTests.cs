using Microsoft.Data.Sqlite;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task MigrateAsyncBacksUpExistingDataBeforeUpgrade()
    {
        using var fixture = new MigrationFixture();
        await fixture.ExecuteAsync("CREATE TABLE legacy_data(value TEXT NOT NULL); INSERT INTO legacy_data(value) VALUES ('preserved');");
        var options = new SqliteDatabaseOptions(fixture.ConnectionString, BackupDirectory: fixture.BackupDirectory);
        var runner = new SqliteMigrationRunner(options, new SqliteBackupService(options, TimeProvider.System),
            [new SqliteMigration(1, "CREATE TABLE schema_version(version INTEGER NOT NULL); CREATE TABLE migrated(value TEXT);")]);

        await runner.MigrateAsync(CancellationToken.None);

        string backupPath = Assert.Single(Directory.GetFiles(fixture.BackupDirectory, "*.db"));
        Assert.True(File.Exists(backupPath));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT version FROM schema_version;"));
    }

    [Fact]
    public async Task MigrateAsyncRollsBackFailedMigration()
    {
        using var fixture = new MigrationFixture();
        var options = new SqliteDatabaseOptions(fixture.ConnectionString, BackupDirectory: fixture.BackupDirectory);
        var runner = new SqliteMigrationRunner(options, new SqliteBackupService(options, TimeProvider.System),
            [new SqliteMigration(1, "CREATE TABLE schema_version(version INTEGER NOT NULL); CREATE TABLE first_table(value TEXT); invalid SQL;")]);

        await Assert.ThrowsAsync<SqliteException>(() => runner.MigrateAsync(CancellationToken.None));

        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'first_table';"));
    }

    private sealed class MigrationFixture : IDisposable
    {
        public MigrationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"moyai-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "moyai.db");
            BackupDirectory = Path.Combine(Root, "backups");
        }

        private string Root { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
