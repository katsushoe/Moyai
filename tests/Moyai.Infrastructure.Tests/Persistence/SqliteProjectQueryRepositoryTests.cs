using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteProjectQueryRepositoryTests
{
    [Fact]
    public async Task SearchIndexesTitleDescriptionAndCommentsWithFiltersAndPagination()
    {
        using var fixture = new QueryFixture();
        QueryServices services = await fixture.CreateServicesAsync();
        await CreateProjectAsync(services.Projects);
        WorkItem first = await services.Items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Bug, "Alpha failure", "agent", "codex"));
        WorkItem second = await services.Items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Feature, "Alpha capability", "agent", "codex"));
        WorkItem third = await services.Items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Issue, "Unrelated", "agent", "codex"));
        await services.Items.UpdateAsync(new UpdateWorkItemCommand("Moyai", third.Key, third.Title, "Contains nebula phrase", WorkItemPriority.High, null, "owner-a", null, third.Revision, "agent", "codex"));
        await services.Collaboration.AddCommentAsync("Moyai", first.Key, "Observed quasar signature", "agent", "codex");

        PagedResult<WorkItem> title = await services.Queries.SearchAsync(new WorkItemSearchRequest("Moyai", "alpha", Offset: 0, Limit: 1));
        PagedResult<WorkItem> description = await services.Queries.SearchAsync(new WorkItemSearchRequest("Moyai", "nebula", Priority: WorkItemPriority.High, Owner: "owner-a"));
        PagedResult<WorkItem> comment = await services.Queries.SearchAsync(new WorkItemSearchRequest("Moyai", "quasar", Type: WorkItemType.Bug));

        Assert.Equal(2, title.Total);
        Assert.Single(title.Items);
        Assert.Equal(third.Id, Assert.Single(description.Items).Id);
        Assert.Equal(first.Id, Assert.Single(comment.Items).Id);

        await services.Items.SetDeletedAsync("Moyai", first.Key, first.Revision, true, "agent", "codex");
        Assert.Empty((await services.Queries.SearchAsync(new WorkItemSearchRequest("Moyai", "quasar"))).Items);
        Assert.Equal(second.Id, Assert.Single((await services.Queries.SearchAsync(new WorkItemSearchRequest("Moyai", "alpha"))).Items).Id);
    }

    [Fact]
    public async Task OverviewAndChangesSinceReturnDeterministicProjectState()
    {
        using var fixture = new QueryFixture();
        QueryServices services = await fixture.CreateServicesAsync();
        Project project = await CreateProjectAsync(services.Projects);
        DateTimeOffset since = DateTimeOffset.UtcNow.AddSeconds(-1);
        WorkItem blocker = await services.Items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Bug, "Blocker", "agent", "codex"));
        WorkItem blocked = await services.Items.CreateAsync(new CreateWorkItemCommand("Moyai", WorkItemType.Feature, "Blocked", "agent", "codex"));
        await services.Collaboration.AddRelationAsync("Moyai", blocker.Key, blocked.Key, "blocks", "agent", "codex");
        await fixture.InsertReleasesAsync(project.Id);

        ProjectOverview overview = await services.Queries.GetOverviewAsync("Moyai", 3);
        ProjectChanges firstPage = await services.Queries.GetChangesSinceAsync("Moyai", since, 0, 2);
        ProjectChanges secondPage = await services.Queries.GetChangesSinceAsync("Moyai", since, 2, 2);

        Assert.Equal(1, overview.OpenWorkItems["bug"]);
        Assert.Equal(1, overview.OpenWorkItems["feature"]);
        Assert.Equal(blocker.Id, Assert.Single(overview.Blockers).Id);
        Assert.Equal("1.0.0", overview.LatestStableRelease);
        Assert.Equal("1.1.0", overview.PlannedRelease);
        Assert.Equal(3, overview.RecentlyChanged.Count);
        Assert.True(firstPage.Total >= 4);
        Assert.Equal(2, firstPage.Events.Count);
        Assert.Equal(2, secondPage.Events.Count);
        Assert.True(firstPage.Events[^1].CreatedAt <= secondPage.Events[0].CreatedAt);
    }

    private static Task<Project> CreateProjectAsync(ProjectService service) => service.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "csharp", "local", "agent", "codex"));

    private sealed record QueryServices(ProjectService Projects, WorkItemService Items, WorkItemCollaborationService Collaboration, ProjectQueryService Queries);

    private sealed class QueryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-query-{Guid.NewGuid():N}");
        private string DatabasePath => Path.Combine(_root, "moyai.db");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();

        public async Task<QueryServices> CreateServicesAsync()
        {
            Directory.CreateDirectory(_root);
            var options = new SqliteDatabaseOptions(ConnectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projects = new SqliteProjectRepository(options);
            var items = new SqliteWorkItemRepository(options);
            return new QueryServices(new ProjectService(projects, TimeProvider.System), new WorkItemService(projects, items, TimeProvider.System), new WorkItemCollaborationService(projects, items, new SqliteWorkItemCollaborationRepository(options), TimeProvider.System), new ProjectQueryService(projects, new SqliteProjectQueryRepository(options)));
        }

        public async Task InsertReleasesAsync(Guid projectId)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using SqliteCommand command = connection.CreateCommand();
            string now = DateTimeOffset.UtcNow.ToString("O");
            command.CommandText = $"""
                INSERT INTO releases(id,project_id,version,channel,status,released_at,created_at,updated_at,revision) VALUES('{Guid.NewGuid():D}','{projectId:D}','1.0.0','stable','released','{now}','{now}','{now}',1);
                INSERT INTO releases(id,project_id,version,channel,status,planned_at,created_at,updated_at,revision) VALUES('{Guid.NewGuid():D}','{projectId:D}','1.1.0','stable','planned','{now}','{now}','{now}',1);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
