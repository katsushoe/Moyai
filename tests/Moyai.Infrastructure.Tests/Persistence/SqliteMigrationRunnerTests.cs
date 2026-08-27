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
        Assert.Equal("preserved", await MigrationFixture.BackupScalarAsync(backupPath, "SELECT value FROM legacy_data;"));
    }

    [Fact]
    public async Task MigrateAsyncRollsBackFailedMigration()
    {
        using var fixture = new MigrationFixture();
        await fixture.ExecuteAsync("CREATE TABLE legacy_data(value TEXT NOT NULL); INSERT INTO legacy_data(value) VALUES ('unchanged');");
        var options = new SqliteDatabaseOptions(fixture.ConnectionString, BackupDirectory: fixture.BackupDirectory);
        var runner = new SqliteMigrationRunner(options, new SqliteBackupService(options, TimeProvider.System),
            [new SqliteMigration(1, "CREATE TABLE schema_version(version INTEGER NOT NULL); CREATE TABLE first_table(value TEXT); invalid SQL;")]);

        await Assert.ThrowsAsync<SqliteException>(() => runner.MigrateAsync(CancellationToken.None));

        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'first_table';"));
        Assert.Equal("unchanged", await fixture.ScalarTextAsync("SELECT value FROM legacy_data;"));
        Assert.Single(Directory.GetFiles(fixture.BackupDirectory, "*.db"));
    }

    [Fact]
    public async Task MigrateAsyncRejectsNewerDatabaseWithoutModification()
    {
        using var fixture = new MigrationFixture();
        await fixture.ExecuteAsync("CREATE TABLE schema_version(version INTEGER NOT NULL); INSERT INTO schema_version(version) VALUES (2); CREATE TABLE existing(value TEXT); INSERT INTO existing(value) VALUES ('kept');");
        var options = new SqliteDatabaseOptions(fixture.ConnectionString, BackupDirectory: fixture.BackupDirectory);
        var runner = new SqliteMigrationRunner(options, new SqliteBackupService(options, TimeProvider.System),
            [new SqliteMigration(1, "CREATE TABLE migrated(value TEXT);")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync(CancellationToken.None));

        Assert.Equal("kept", await fixture.ScalarTextAsync("SELECT value FROM existing;"));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
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

        public async Task<string> ScalarTextAsync(string sql) =>
            await ScalarTextFromAsync(ConnectionString, sql);

        public static async Task<string> BackupScalarAsync(string backupPath, string sql)
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            return await ScalarTextFromAsync(connectionString, sql);
        }

        private static async Task<string> ScalarTextFromAsync(string connectionString, string sql)
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
