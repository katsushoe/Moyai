using Microsoft.Data.Sqlite;
using Moyai.Application.Persistence;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public async Task CreateAndListReturnValidReadableBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "moyai.db");
        string backupDirectory = Path.Combine(root, "backups");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            await CreateSourceAsync(connectionString);
            var options = new SqliteDatabaseOptions(connectionString, BackupDirectory: backupDirectory);
            var service = new SqliteBackupService(options, TimeProvider.System);

            string path = await service.CreateAsync(CancellationToken.None);
            IReadOnlyList<DatabaseBackupInfo> backups = await service.ListAsync(CancellationToken.None);

            DatabaseBackupInfo backup = Assert.Single(backups);
            Assert.Equal(path, backup.Path);
            Assert.True(backup.IsValid);
            Assert.True(backup.SizeBytes > 0);
            Assert.Equal("source", await ReadValueAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task CreateSourceAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE sample(value TEXT NOT NULL); INSERT INTO sample(value) VALUES ('source');";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string> ReadValueAsync(string path)
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM sample;";
        return Convert.ToString(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
