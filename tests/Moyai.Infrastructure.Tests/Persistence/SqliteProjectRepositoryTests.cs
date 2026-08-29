using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Projects;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteProjectRepositoryTests
{
    [Fact]
    public async Task ProjectServiceSupportsCrudArchivingAndEventHistory()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        var create = new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex");

        Project created = await service.CreateAsync(create, CancellationToken.None);
        Project loaded = await service.GetAsync("MOYAI", CancellationToken.None);
        var update = new UpdateProjectCommand("Moyai", "Moyai Next", "https://bitbucket.org/example/moyai", null, "Description", "{}", "Moyai", "moyai@example.com", "origin", "develop", created.Revision, "agent", "codex");
        Project updated = await service.UpdateAsync(update, CancellationToken.None);
        Project archived = await service.SetArchivedAsync(updated.Name, updated.Revision, true, "agent", "codex", CancellationToken.None);

        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal("Moyai Next", updated.Name);
        Assert.Equal("https://bitbucket.org/example/moyai", updated.RepositoryUrl);
        Assert.Equal("bitbucket", updated.RepositoryProvider);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Empty(await service.ListAsync(false, CancellationToken.None));
        Assert.Single(await service.ListAsync(true, CancellationToken.None));
        Assert.Equal(3L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task CreateAsyncRejectsCaseInsensitiveDuplicateNameWithoutEvent()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        var first = new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex");
        var duplicate = first with { Name = "MOYAI" };
        await service.CreateAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<ProjectNameConflictException>(() => service.CreateAsync(duplicate, CancellationToken.None));

        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM projects;"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task ProjectOperationsUseOrdinalCaseInsensitiveCanonicalName()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        Project created = await service.CreateAsync(new CreateProjectCommand("MoyaiÅ", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"));

        Project loaded = await service.GetAsync("moyaiå");
        Project updated = await service.UpdateAsync(new UpdateProjectCommand("MOYAIÅ", "MoyaiÅ", null, null, "updated", null, null, null, "origin", null, created.Revision, "agent", "codex"));

        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal("MoyaiÅ", loaded.Name);
        Assert.Equal("updated", updated.Description);
        Assert.Equal(created.Id, updated.Id);
    }

    [Fact]
    public async Task CreateAsyncRejectsOrdinalCaseInsensitiveUnicodeDuplicate()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        var first = new CreateProjectCommand("MoyaiÅ", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex");
        await service.CreateAsync(first);

        await Assert.ThrowsAsync<ProjectNameConflictException>(() => service.CreateAsync(first with { Name = "moyaiå" }));

        Assert.Single(await service.ListAsync(true));
    }

    [Fact]
    public async Task UpdateAsyncRejectsStaleRevisionWithoutEventOrDataChange()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        Project created = await service.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"), CancellationToken.None);
        var update = new UpdateProjectCommand("Moyai", "Changed", null, null, null, null, null, null, "origin", null, created.Revision + 1, "agent", "codex");

        await Assert.ThrowsAsync<RevisionConflictException>(() => service.UpdateAsync(update, CancellationToken.None));

        Assert.Equal("Moyai", (await service.GetAsync("Moyai", CancellationToken.None)).Name);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    private sealed class ProjectFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-project-{Guid.NewGuid():N}");
        private string DatabasePath => Path.Combine(_root, "moyai.db");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

        public async Task<ProjectService> CreateServiceAsync()
        {
            Directory.CreateDirectory(_root);
            var options = new SqliteDatabaseOptions(ConnectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            return new ProjectService(new SqliteProjectRepository(options), TimeProvider.System);
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
