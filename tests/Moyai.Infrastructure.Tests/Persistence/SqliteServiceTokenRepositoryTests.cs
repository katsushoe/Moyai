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
}
