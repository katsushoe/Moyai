using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Domain.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteServiceTokenRepositoryTests
{
    [Fact]
    public async Task RepositorySupportsIntrospectionAndExpirationCleanup()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"moyai-auth-{Guid.NewGuid():N}.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var repository = new SqliteServiceTokenRepository(options);
            ServiceToken token = ServiceToken.Issue("githubbie", ["repository.write"], DateTimeOffset.UtcNow.AddMinutes(10), TimeProvider.System);
            await repository.AddAsync(token, CancellationToken.None);
            var service = new AuthIntrospectionService(repository, TimeProvider.System);

            AuthIntrospectionResult result = await service.IntrospectAsync(token.Token, "githubbie", "repository.write", CancellationToken.None);

            Assert.True(result.Valid);
            ServiceToken? stored = await repository.FindByTokenAsync(token.Token, CancellationToken.None);
            Assert.NotNull(stored?.LastUsedAt);
            Assert.Equal(1, await repository.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));
            Assert.Null(await repository.FindByTokenAsync(token.Token, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task LifecycleServiceRotatesAndRevokesWithoutTokenInEvents()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"moyai-auth-lifecycle-{Guid.NewGuid():N}.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var repository = new SqliteServiceTokenRepository(options);
            var service = new ServiceTokenLifecycleService(repository, TimeProvider.System);
            ServiceToken issued = await service.IssueAsync("githubbie", ["repository.write"], DateTimeOffset.UtcNow.AddHours(1), "admin", "test");

            ServiceToken rotated = await service.RotateAsync("githubbie", ["repository.write"], DateTimeOffset.UtcNow.AddHours(1), "admin", "test");
            bool revoked = await service.RevokeAsync("githubbie", "admin", "test");

            Assert.True(revoked);
            Assert.Null(await repository.FindByTokenAsync(issued.Token));
            Assert.Null(await repository.FindByTokenAsync(rotated.Token));
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), COALESCE(SUM(CASE WHEN after_json LIKE '%' || $issued || '%' OR after_json LIKE '%' || $rotated || '%' THEN 1 ELSE 0 END),0) FROM events WHERE entity_type='service_token';";
            command.Parameters.AddWithValue("$issued", issued.Token);
            command.Parameters.AddWithValue("$rotated", rotated.Token);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt64(0));
            Assert.Equal(0, reader.GetInt64(1));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task DeleteExpiredWithEventsAsyncPhysicallyDeletesAndAuditsToken()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"moyai-auth-expired-{Guid.NewGuid():N}.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        try
        {
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var repository = new SqliteServiceTokenRepository(options);
            ServiceToken token = ServiceToken.Issue("buckettie", ["repository.write"], DateTimeOffset.UtcNow.AddMinutes(10), TimeProvider.System);
            await repository.IssueWithEventAsync(token, "admin", "test");

            int deleted = await repository.DeleteExpiredWithEventsAsync(DateTimeOffset.UtcNow.AddHours(1), "system", "cleanup");

            Assert.Equal(1, deleted);
            Assert.Null(await repository.FindByTokenAsync(token.Token));
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM events WHERE event_type='service_token_expired' AND entity_id=$id;";
            command.Parameters.AddWithValue("$id", token.Id.ToString("D"));
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
