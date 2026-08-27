using Microsoft.Data.Sqlite;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsyncCreatesCoreTables()
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
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('projects', 'work_items', 'events', 'service_tokens');";
            object? count = await command.ExecuteScalarAsync(CancellationToken.None);
            Assert.Equal(4L, count);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
