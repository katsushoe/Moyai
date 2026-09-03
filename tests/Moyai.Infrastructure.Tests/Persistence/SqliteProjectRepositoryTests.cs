using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Projects;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteProjectRepositoryTests
{
    [Fact]
    public async Task RenameAsyncPreservesAllOtherFieldsAndAuditHistory()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        await service.CreateAsync(new CreateProjectCommand("Original", "source", "install", "https://github.com/example/repo", null, "csharp", "local"));
        await service.UpdateAsync(new UpdateProjectCommand("Original", "Original", null, null, "Description", "{\"setting\":true}", "User", "user@example.com", "upstream", "develop", 1, "test", "tester"));
        Project before = await service.SetArchivedAsync("Original", 2, true, "test", "tester");

        Project renamed = await service.RenameAsync("ORIGINAL", "Renamed", before.Revision, "test", "renamer");
        Project loaded = await service.GetAsync("renamed");

        var expected = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(before))!;
        var actual = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(loaded))!;
        expected["Name"] = "Renamed";
        expected["Revision"] = before.Revision + 1;
        expected["UpdatedAt"] = actual["UpdatedAt"]!.DeepClone();
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(expected, actual));
        Assert.Equal(renamed.Id, loaded.Id);
        Assert.True(loaded.UpdatedAt >= before.UpdatedAt);
        await Assert.ThrowsAsync<ProjectNotFoundException>(() => service.GetAsync("Original"));
        Assert.Equal(4L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events WHERE event_type = 'project_renamed' AND actor_name = 'renamer';"));
    }

    [Fact]
    public async Task RenameAsyncRejectsConflictsAndInvalidNamesWithoutChanges()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        await service.CreateAsync(new CreateProjectCommand("Original"));
        await service.CreateAsync(new CreateProjectCommand("OtherÅ"));

        await Assert.ThrowsAsync<ProjectNameConflictException>(() => service.RenameAsync("Original", "otherå", 1));
        await Assert.ThrowsAsync<RevisionConflictException>(() => service.RenameAsync("Original", "New", 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync("Original", " ", 1));
        await Assert.ThrowsAsync<ProjectNotFoundException>(() => service.RenameAsync("Missing", "New", 1));
        Assert.Equal(1, (await service.GetAsync("Original")).Revision);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));

        Project renamed = await service.RenameAsync("Original", "ORIGINAL", 1);
        Assert.Equal("ORIGINAL", (await service.GetAsync("original")).Name);
        Assert.Equal(2, renamed.Revision);
    }

    [Fact]
    public async Task NameOnlyProjectCanBeConfiguredLaterWithoutLosingSettings()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        Project created = await service.CreateAsync(new CreateProjectCommand("Tracking"));
        Assert.Empty(created.SourcePath);
        Assert.Empty(created.RepositoryProvider);
        Project configured = await service.ConfigureAsync("Tracking", 1, "source", "install", "https://github.com/example/repo", null, "csharp", "local", "test", "tester");
        Project loaded = await service.GetAsync("Tracking");
        Assert.Equal("source", loaded.SourcePath);
        Assert.Equal("install", loaded.InstallPath);
        Assert.Equal("github", loaded.RepositoryProvider);
        Assert.Equal("csharp", loaded.BuildProvider);
        Assert.Equal("local", loaded.DeployMode);
        await service.ConfigureAsync("Tracking", configured.Revision, null, null, null, null, null, "server", "test", "tester");
        loaded = await service.GetAsync("Tracking");
        Assert.Equal("source", loaded.SourcePath);
        Assert.Equal("github", loaded.RepositoryProvider);
        Assert.Equal("server", loaded.DeployMode);
        await Assert.ThrowsAsync<RevisionConflictException>(() => service.ConfigureAsync("Tracking", 1, "wrong", null, null, null, null, null, "test", "tester"));
        Assert.Equal("source", (await service.GetAsync("Tracking")).SourcePath);
        Assert.Equal(3L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task EnsureAsyncCreatesOnceAndPreservesArchivedProject()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        Project[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.EnsureAsync("Concurrent")));
        Assert.Single(results.Select(value => value.Id).Distinct());
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
        Project archived = await service.SetArchivedAsync("Concurrent", 1, true, "test", "tester");
        Project ensured = await service.EnsureAsync("CONCURRENT");
        Assert.Equal(archived.Id, ensured.Id);
        Assert.Equal(archived.Revision, ensured.Revision);
        Assert.NotNull(ensured.ArchivedAt);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

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
    public async Task GetAsyncReturnsRegisteredCandidatesWhenProjectIsNotRegistered()
    {
        using var fixture = new ProjectFixture();
        ProjectService service = await fixture.CreateServiceAsync();
        await service.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"));
        await service.CreateAsync(new CreateProjectCommand("Kelpie", "source", "install", "https://github.com/example/kelpie", null, "csharp", "local", "agent", "codex"));

        ProjectNotFoundException exception = await Assert.ThrowsAsync<ProjectNotFoundException>(() => service.GetAsync("Unknown"));

        Assert.Equal("Unknown", exception.ProjectName);
        Assert.Equal(["Kelpie", "Moyai"], exception.Candidates);
        Assert.Contains("Registered project candidates: Kelpie, Moyai", exception.Message, StringComparison.Ordinal);
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
