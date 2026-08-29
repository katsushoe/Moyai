using Microsoft.Data.Sqlite;
using Moyai.Domain.Events;
using Moyai.Domain.Releases;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteReleaseContentRepositoryTests
{
    [Fact]
    public async Task AddListAndRemovePersistContentAndAuditEvents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-release-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var repository = new SqliteReleaseContentRepository(options);
            Guid projectId = Guid.NewGuid();
            Guid releaseId = Guid.NewGuid();
            ReleaseWorkItem relation = ReleaseWorkItem.Create(projectId, releaseId, Guid.NewGuid(), "fixes", TimeProvider.System);
            ReleaseArtifact artifact = ReleaseArtifact.Create(projectId, releaseId, null, "Windows MSI", "installer", "windows", "x64", "Moyai.msi", "dist/Moyai.msi", null, 2048, "abc", "dist/Moyai.msi.sig", null, TimeProvider.System);

            await SeedParentsAsync(connectionString, projectId, releaseId, relation.WorkItemId);
            await repository.AddWorkItemAsync(relation, Event(projectId, releaseId, "release_item_added"));
            await repository.AddArtifactAsync(artifact, Event(projectId, releaseId, "artifact_added"));

            Assert.Equal(relation, Assert.Single(await repository.ListWorkItemsAsync(projectId, releaseId)));
            Assert.Equal(artifact, Assert.Single(await repository.ListArtifactsAsync(projectId, releaseId)));
            Assert.True(await repository.RemoveWorkItemAsync(projectId, releaseId, relation.Id, Event(projectId, releaseId, "release_item_removed")));
            Assert.True(await repository.RemoveArtifactAsync(projectId, releaseId, artifact.Id, Event(projectId, releaseId, "artifact_removed")));
            Assert.Empty(await repository.ListWorkItemsAsync(projectId, releaseId));
            Assert.Empty(await repository.ListArtifactsAsync(projectId, releaseId));
            Assert.Equal(4L, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM events;"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ProjectEvent Event(Guid projectId, Guid releaseId, string eventType) =>
        ProjectEvent.Create(projectId, "release", releaseId, eventType, "agent", "test", null, null, null, TimeProvider.System);

    private static async Task SeedParentsAsync(string connectionString, Guid projectId, Guid releaseId, Guid workItemId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO projects(id,name,source_path,repository_url,repository_provider,build_provider,deploy_mode,git_remote_name,created_at,updated_at,revision) VALUES($project,'Test','src','https://example.test/repository','github','local','local','origin',$now,$now,1); INSERT INTO releases(id,project_id,version,channel,status,revision,created_at,updated_at) VALUES($release,$project,'1.0.0','stable','draft',1,$now,$now); INSERT INTO work_items(id,project_id,type,key,sequence_no,title,status,priority,created_by_type,created_by_name,revision,created_at,updated_at) VALUES($item,$project,'bug','BUG-1',1,'Bug','open','medium','agent','test',1,$now,$now);";
        command.Parameters.AddWithValue("$project", projectId.ToString("D"));
        command.Parameters.AddWithValue("$release", releaseId.ToString("D"));
        command.Parameters.AddWithValue("$item", workItemId.ToString("D"));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
