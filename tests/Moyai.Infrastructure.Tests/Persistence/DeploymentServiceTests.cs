using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moyai.Application.Builds;
using Moyai.Application.Deployments;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Domain.Builds;
using Moyai.Domain.Deployments;
using Moyai.Domain.Events;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class DeploymentServiceTests
{
    [Fact]
    public async Task LocalDeployVerifiesArtifactAndRollbackRestoresPreviousContent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-deploy-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source"); string install = Path.Combine(root, "install"); Directory.CreateDirectory(source); Directory.CreateDirectory(install); await File.WriteAllTextAsync(Path.Combine(install, "old.txt"), "old"); string artifactPath = Path.Combine(source, "app.bin"); await File.WriteAllTextAsync(artifactPath, "new");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString()); await new SqliteDatabaseInitializer(options).InitializeAsync(); var projects = new SqliteProjectRepository(options); Moyai.Domain.Projects.Project project = await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", source, install, "https://github.com/example/moyai", null, "csharp", "local", "agent", "test"));
            var buildRepository = new SqliteBuildRepository(options); Build build = Build.Create(project.Id, "csharp", "abc123", "Release", null, "agent", "test", TimeProvider.System); await buildRepository.AddAsync(build, Event(project.Id, build.Id, "build_started")); long revision = build.Revision; build.Start(TimeProvider.System); await buildRepository.UpdateAsync(build, revision, Event(project.Id, build.Id, "build_started")); revision = build.Revision; build.Succeed(TimeProvider.System); await buildRepository.UpdateAsync(build, revision, Event(project.Id, build.Id, "build_succeeded")); await using FileStream stream = File.OpenRead(artifactPath); string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(); var artifact = new BuildArtifact(Guid.NewGuid(), project.Id, build.Id, "app", "binary", "file", "app.bin", new FileInfo(artifactPath).Length, hash, null, DateTimeOffset.UtcNow); await buildRepository.AddArtifactAsync(artifact, Event(project.Id, build.Id, "artifact_added"));
            var lifecycle = new LifecycleService(projects, new SqliteServiceTokenRepository(options), [], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System); var service = new DeploymentService(projects, buildRepository, new SqliteReleaseRepository(options), new SqliteDeploymentRepository(options), lifecycle, TimeProvider.System);

            Deployment deployment = await service.StartAsync("Moyai", build.Id, artifact.Id, null, "agent", "test");
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(install, "app.bin")));
            Deployment rollback = await service.RollbackAsync("Moyai", deployment.Id, "agent", "test");

            Assert.Equal(DeploymentStatus.Succeeded, deployment.Status); Assert.Equal(DeploymentStatus.RolledBack, rollback.Status); Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(install, "old.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static ProjectEvent Event(Guid projectId, Guid entityId, string type) => ProjectEvent.Create(projectId, "build", entityId, type, "agent", "test", null, null, null, TimeProvider.System);
}
