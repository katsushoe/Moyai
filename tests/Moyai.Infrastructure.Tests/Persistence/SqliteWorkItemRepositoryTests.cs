using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteWorkItemRepositoryTests
{
    [Fact]
    public async Task WorkItemServiceAssignsKeysByProjectAndTypeAndSupportsUpdate()
    {
        using var fixture = new WorkItemFixture();
        (ProjectService projects, WorkItemService items) = await fixture.CreateServicesAsync();
        await CreateProjectAsync(projects, "Moyai");

        WorkItem firstBug = await items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Bug, "First bug", "agent", "codex"));
        WorkItem secondBug = await items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Bug, "Second bug", "agent", "codex"));
        WorkItem feature = await items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Feature, "Feature", "agent", "codex"));
        var update = new UpdateWorkItemCommand("Moyai", firstBug.Key, "Fixed title", "Details", WorkItemPriority.High, WorkItemSeverity.Major, "owner", "{}", firstBug.Revision, "agent", "codex");

        WorkItem updated = await items.UpdateAsync(update);

        Assert.Equal("BUG-1", firstBug.Key);
        Assert.Equal("BUG-2", secondBug.Key);
        Assert.Equal("FEAT-1", feature.Key);
        Assert.Equal("Fixed title", updated.Title);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(updated.Id, (await items.GetAsync("Moyai", "bug-1")).Id);
        Assert.Equal(3, (await items.ListAsync("Moyai")).Count);
        Assert.Equal(5L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task SetDeletedAsyncSoftDeletesAndRestoresItem()
    {
        using var fixture = new WorkItemFixture();
        (ProjectService projects, WorkItemService items) = await fixture.CreateServicesAsync();
        await CreateProjectAsync(projects, "Moyai");
        WorkItem created = await items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Decision, "Decision", "agent", "codex"));

        WorkItem deleted = await items.SetDeletedAsync("Moyai", created.Key, created.Revision, true, "agent", "codex");

        Assert.Empty(await items.ListAsync("Moyai"));
        Assert.Single(await items.ListAsync("Moyai", includeDeleted: true));
        WorkItem restored = await items.SetDeletedAsync("Moyai", deleted.Key, deleted.Revision, false, "agent", "codex");
        Assert.Null(restored.DeletedAt);
        Assert.Single(await items.ListAsync("Moyai"));
        Assert.Equal(4L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task UpdateAsyncRejectsStaleRevisionWithoutEventOrDataChange()
    {
        using var fixture = new WorkItemFixture();
        (ProjectService projects, WorkItemService items) = await fixture.CreateServicesAsync();
        await CreateProjectAsync(projects, "Moyai");
        WorkItem created = await items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Issue, "Original", "agent", "codex"));
        var update = new UpdateWorkItemCommand("Moyai", created.Key, "Changed", null, WorkItemPriority.Normal, null, null, null, created.Revision + 1, "agent", "codex");

        await Assert.ThrowsAsync<RevisionConflictException>(() => items.UpdateAsync(update));

        Assert.Equal("Original", (await items.GetAsync("Moyai", created.Key)).Title);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    private static Task<Project> CreateProjectAsync(ProjectService service, string name) => service.CreateAsync(
        new CreateProjectCommand(name, "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"));

    private sealed class WorkItemFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-work-item-{Guid.NewGuid():N}");
        private string DatabasePath => Path.Combine(_root, "moyai.db");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

        public async Task<(ProjectService Projects, WorkItemService Items)> CreateServicesAsync()
        {
            Directory.CreateDirectory(_root);
            var options = new SqliteDatabaseOptions(ConnectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projectRepository = new SqliteProjectRepository(options);
            return (new ProjectService(projectRepository, TimeProvider.System), new WorkItemService(projectRepository, new SqliteWorkItemRepository(options), TimeProvider.System));
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
