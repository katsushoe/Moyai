using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteWorkItemCollaborationRepositoryTests
{
    [Fact]
    public async Task CollaborationOperationsPersistAtomicallyAndAppearInHistory()
    {
        using var fixture = new CollaborationFixture();
        (ProjectService projects, WorkItemService items, WorkItemCollaborationService collaboration) = await fixture.CreateServicesAsync();
        await CreateProjectAsync(projects);
        WorkItem first = await CreateItemAsync(items, "First");
        WorkItem second = await CreateItemAsync(items, "Second");

        RelationAddResult relation = await collaboration.AddRelationAsync("Moyai", first.Key, second.Key, "depends_on", "agent", "codex");
        RelationAddResult cycle = await collaboration.AddRelationAsync("Moyai", second.Key, first.Key, "blocks", "agent", "codex");
        WorkItemComment comment = await collaboration.AddCommentAsync("Moyai", first.Key, "Investigation result", "agent", "codex");
        WorkItemTaskLink task = await collaboration.AddTaskLinkAsync("Moyai", first.Key, "hataori", "TASK-101", "implements", "agent", "codex");
        WorkItemCommitLink commit = await collaboration.AddCommitLinkAsync("Moyai", first.Key, "abc123", "fixes", "agent", "codex");

        Assert.Empty(relation.Warnings);
        Assert.Contains("relation_cycle_detected", cycle.Warnings);
        Assert.Equal(comment, Assert.Single(await collaboration.ListCommentsAsync("Moyai", first.Key)));
        Assert.Equal(task, Assert.Single(await collaboration.ListTaskLinksAsync("Moyai", first.Key)));
        Assert.Equal(commit, Assert.Single(await collaboration.ListCommitLinksAsync("Moyai", first.Key)));
        Assert.Equal(2, (await collaboration.ListRelationsAsync("Moyai", first.Key)).Count);

        Assert.True(await collaboration.RemoveTaskLinkAsync("Moyai", task.Id, "agent", "codex"));
        Assert.True(await collaboration.RemoveCommitLinkAsync("Moyai", commit.Id, "agent", "codex"));
        Assert.True(await collaboration.RemoveRelationAsync("Moyai", relation.Relation.Id, "agent", "codex"));
        Assert.False(await collaboration.RemoveTaskLinkAsync("Moyai", task.Id, "agent", "codex"));

        string[] eventTypes = (await collaboration.ListHistoryAsync("Moyai", first.Key)).Select(static value => value.EventType).ToArray();
        Assert.Contains("item_created", eventTypes);
        Assert.Contains("relation_added", eventTypes);
        Assert.Contains("relation_removed", eventTypes);
        Assert.Contains("comment_added", eventTypes);
        Assert.Contains("task_link_removed", eventTypes);
        Assert.Contains("commit_link_removed", eventTypes);
        Assert.Equal(11L, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
    }

    [Fact]
    public async Task SymmetricRelationRejectsReverseDuplicateWithoutAuditEvent()
    {
        using var fixture = new CollaborationFixture();
        (ProjectService projects, WorkItemService items, WorkItemCollaborationService collaboration) = await fixture.CreateServicesAsync();
        await CreateProjectAsync(projects);
        WorkItem first = await CreateItemAsync(items, "First");
        WorkItem second = await CreateItemAsync(items, "Second");
        await collaboration.AddRelationAsync("Moyai", first.Key, second.Key, "relates_to", "agent", "codex");
        long eventsBefore = await fixture.ScalarAsync("SELECT COUNT(*) FROM events;");

        await Assert.ThrowsAsync<SqliteException>(() => collaboration.AddRelationAsync("Moyai", second.Key, first.Key, "relates_to", "agent", "codex"));

        Assert.Equal(eventsBefore, await fixture.ScalarAsync("SELECT COUNT(*) FROM events;"));
        Assert.Single(await collaboration.ListRelationsAsync("Moyai", first.Key));
    }

    private static Task<Project> CreateProjectAsync(ProjectService service) => service.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"));
    private static Task<WorkItem> CreateItemAsync(WorkItemService service, string title) => service.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Bug, title, "agent", "codex"));

    private sealed class CollaborationFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-collaboration-{Guid.NewGuid():N}");
        private string DatabasePath => Path.Combine(_root, "moyai.db");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

        public async Task<(ProjectService Projects, WorkItemService Items, WorkItemCollaborationService Collaboration)> CreateServicesAsync()
        {
            Directory.CreateDirectory(_root);
            var options = new SqliteDatabaseOptions(ConnectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projects = new SqliteProjectRepository(options);
            var items = new SqliteWorkItemRepository(options);
            return (new ProjectService(projects, TimeProvider.System), new WorkItemService(projects, items, TimeProvider.System), new WorkItemCollaborationService(projects, items, new SqliteWorkItemCollaborationRepository(options), TimeProvider.System));
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
