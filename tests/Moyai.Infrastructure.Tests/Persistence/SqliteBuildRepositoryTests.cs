using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Builds;
using Moyai.Domain.Events;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class SqliteBuildRepositoryTests
{
    [Fact]
    public async Task AddUpdateGetAndListPersistBuildWithEvents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projectRepository = new SqliteProjectRepository(options);
            Moyai.Domain.Projects.Project project = await new ProjectService(projectRepository, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "agent", "test"));
            var repository = new SqliteBuildRepository(options);
            Build build = Build.Create(project.Id, "dotnet", "abc123", "Release", null, "agent", "test", TimeProvider.System);

            await repository.AddAsync(build, Event(build, "build_started"));
            long revision = build.Revision;
            build.Start(TimeProvider.System);
            await repository.UpdateAsync(build, revision, Event(build, "build_started"));
            revision = build.Revision;
            build.Succeed(TimeProvider.System);
            await repository.UpdateAsync(build, revision, Event(build, "build_succeeded"));

            Assert.Equal(BuildStatus.Succeeded, (await repository.GetAsync(project.Id, build.Id))?.Status);
            Assert.Single(await repository.ListAsync(project.Id));
            Assert.Empty(await repository.ListArtifactsAsync(project.Id, build.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ProjectEvent Event(Build build, string type) => ProjectEvent.Create(build.ProjectId, "build", build.Id, type, "agent", "test", null, null, null, TimeProvider.System);
}
