using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;
using Moyai.Domain.Releases;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteReleaseRepositoryTests
{
    [Fact]
    public async Task AddGetListAndUpdatePersistReleaseAndEvents()
    {
        using var fixture = new ReleaseFixture();
        (Project project, SqliteReleaseRepository repository) = await fixture.CreateAsync();
        Release release = Release.Create(project.Id, "1.0.0", ReleaseChannel.Stable, "draft", TimeProvider.System);
        await repository.AddAsync(release, Event(release, "release_created"));

        release.Update(ReleaseChannel.Rc, "v1.0.0", "abc123", "ready", DateTimeOffset.UtcNow, TimeProvider.System);
        await repository.UpdateAsync(release, 1, Event(release, "release_updated"));

        Release loaded = Assert.IsType<Release>(await repository.GetAsync(project.Id, "1.0.0"));
        Assert.Equal(ReleaseChannel.Rc, loaded.Channel);
        Assert.Equal("abc123", loaded.CommitHash);
        Assert.Single(await repository.ListAsync(project.Id));
        Assert.Equal(3L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task UpdateWithStaleRevisionRollsBackEvent()
    {
        using var fixture = new ReleaseFixture();
        (Project project, SqliteReleaseRepository repository) = await fixture.CreateAsync();
        Release release = Release.Create(project.Id, "1.0.0", ReleaseChannel.Stable, null, TimeProvider.System);
        await repository.AddAsync(release, Event(release, "release_created"));
        release.Update(ReleaseChannel.Beta, null, null, null, null, TimeProvider.System);

        await Assert.ThrowsAsync<RevisionConflictException>(() => repository.UpdateAsync(release, 99, Event(release, "release_updated")));

        Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
        Assert.Equal(ReleaseChannel.Stable, (await repository.GetAsync(project.Id, release.Version))?.Channel);
    }

    private static ProjectEvent Event(Release release, string eventType) => ProjectEvent.Create(release.ProjectId, "release", release.Id, eventType, "agent", "test", null, null, null, TimeProvider.System);

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-release-{Guid.NewGuid():N}");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "moyai.db"), Pooling = false }.ToString();

        public async Task<(Project Project, SqliteReleaseRepository Repository)> CreateAsync()
        {
            Directory.CreateDirectory(_root);
            var options = new SqliteDatabaseOptions(ConnectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projects = new ProjectService(new SqliteProjectRepository(options), TimeProvider.System);
            Project project = await projects.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "test"));
            return (project, new SqliteReleaseRepository(options));
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
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
