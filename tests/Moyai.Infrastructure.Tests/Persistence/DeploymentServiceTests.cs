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

            Assert.Equal(DeploymentStatus.Succeeded, deployment.Status); Assert.Null(deployment.ReleaseId); Assert.Equal(DeploymentStatus.RolledBack, rollback.Status); Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(install, "old.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LocalDeployWithInvalidArtifactHashPersistsFailedStatus()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-deploy-verify-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source"); string install = Path.Combine(root, "install"); Directory.CreateDirectory(source); string artifactPath = Path.Combine(source, "app.bin"); await File.WriteAllTextAsync(artifactPath, "new");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString()); await new SqliteDatabaseInitializer(options).InitializeAsync(); var projects = new SqliteProjectRepository(options); Moyai.Domain.Projects.Project project = await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", source, install, "https://github.com/example/moyai", null, "csharp", "local", "agent", "test"));
            var buildRepository = new SqliteBuildRepository(options); Build build = await SucceededBuild(project.Id, buildRepository); var artifact = new BuildArtifact(Guid.NewGuid(), project.Id, build.Id, "app", "binary", "file", "app.bin", new FileInfo(artifactPath).Length, new string('0', 64), null, DateTimeOffset.UtcNow); await buildRepository.AddArtifactAsync(artifact, Event(project.Id, build.Id, "artifact_added"));
            var lifecycle = new LifecycleService(projects, new SqliteServiceTokenRepository(options), [], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System); var service = new DeploymentService(projects, buildRepository, new SqliteReleaseRepository(options), new SqliteDeploymentRepository(options), lifecycle, TimeProvider.System);

            Deployment deployment = await service.StartAsync("Moyai", build.Id, artifact.Id, null, "agent", "test");

            Assert.Equal(DeploymentStatus.Failed, deployment.Status);
            Assert.Equal("deployment_failed", deployment.ErrorCode);
            Assert.False(Directory.Exists(install));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LocalRollbackWithoutBackupPersistsRollbackFailedStatus()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-deploy-rollback-failure-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source"); string install = Path.Combine(root, "install"); Directory.CreateDirectory(source); string artifactPath = Path.Combine(source, "app.bin"); await File.WriteAllTextAsync(artifactPath, "new");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString()); await new SqliteDatabaseInitializer(options).InitializeAsync(); var projects = new SqliteProjectRepository(options); Moyai.Domain.Projects.Project project = await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", source, install, "https://github.com/example/moyai", null, "csharp", "local", "agent", "test"));
            var buildRepository = new SqliteBuildRepository(options); Build build = await SucceededBuild(project.Id, buildRepository); await using FileStream stream = File.OpenRead(artifactPath); string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(); var artifact = new BuildArtifact(Guid.NewGuid(), project.Id, build.Id, "app", "binary", "file", "app.bin", new FileInfo(artifactPath).Length, hash, null, DateTimeOffset.UtcNow); await buildRepository.AddArtifactAsync(artifact, Event(project.Id, build.Id, "artifact_added"));
            var lifecycle = new LifecycleService(projects, new SqliteServiceTokenRepository(options), [], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System); var repository = new SqliteDeploymentRepository(options); var service = new DeploymentService(projects, buildRepository, new SqliteReleaseRepository(options), repository, lifecycle, TimeProvider.System);
            Deployment deployment = await service.StartAsync("Moyai", build.Id, artifact.Id, null, "agent", "test");

            Deployment rollback = await service.RollbackAsync("Moyai", deployment.Id, "agent", "test");
            Deployment persisted = await service.GetAsync("Moyai", rollback.Id);

            Assert.Equal(DeploymentStatus.RollbackFailed, rollback.Status);
            Assert.Equal(DeploymentStatus.RollbackFailed, persisted.Status);
            Assert.Equal("rollback_failed", persisted.ErrorCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LocalDeployWithReleaseVersionPersistsBuildAndReleaseLineage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-deploy-release-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source"); string install = Path.Combine(root, "install"); Directory.CreateDirectory(source); string artifactPath = Path.Combine(source, "app.bin"); await File.WriteAllTextAsync(artifactPath, "new");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString()); await new SqliteDatabaseInitializer(options).InitializeAsync(); var projects = new SqliteProjectRepository(options); Moyai.Domain.Projects.Project project = await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", source, install, "https://github.com/example/moyai", null, "csharp", "local", "agent", "test"));
            var buildRepository = new SqliteBuildRepository(options); Build build = await SucceededBuild(project.Id, buildRepository); await using FileStream stream = File.OpenRead(artifactPath); string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(); var artifact = new BuildArtifact(Guid.NewGuid(), project.Id, build.Id, "app", "binary", "file", "app.bin", new FileInfo(artifactPath).Length, hash, null, DateTimeOffset.UtcNow); await buildRepository.AddArtifactAsync(artifact, Event(project.Id, build.Id, "artifact_added"));
            var releaseRepository = new SqliteReleaseRepository(options); Moyai.Domain.Releases.Release release = Moyai.Domain.Releases.Release.Create(project.Id, "1.0.0", Moyai.Domain.Releases.ReleaseChannel.Stable, null, TimeProvider.System); await releaseRepository.AddAsync(release, Event(project.Id, release.Id, "release_created"));
            var lifecycle = new LifecycleService(projects, new SqliteServiceTokenRepository(options), [], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System); var service = new DeploymentService(projects, buildRepository, releaseRepository, new SqliteDeploymentRepository(options), lifecycle, TimeProvider.System);

            Deployment deployment = await service.StartAsync("Moyai", build.Id, artifact.Id, "1.0.0", "agent", "test");

            Assert.Equal(DeploymentStatus.Succeeded, deployment.Status);
            Assert.Equal(build.Id, deployment.BuildId);
            Assert.Equal(release.Id, deployment.ReleaseId);
            Assert.Equal(build.SourceCommit, deployment.SourceCommit);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<Build> SucceededBuild(Guid projectId, SqliteBuildRepository repository)
    {
        Build build = Build.Create(projectId, "csharp", "abc123", "Release", null, "agent", "test", TimeProvider.System);
        await repository.AddAsync(build, Event(projectId, build.Id, "build_started"));
        long revision = build.Revision; build.Start(TimeProvider.System); await repository.UpdateAsync(build, revision, Event(projectId, build.Id, "build_started"));
        revision = build.Revision; build.Succeed(TimeProvider.System); await repository.UpdateAsync(build, revision, Event(projectId, build.Id, "build_succeeded"));
        return build;
    }

    private static ProjectEvent Event(Guid projectId, Guid entityId, string type) => ProjectEvent.Create(projectId, "build", entityId, type, "agent", "test", null, null, null, TimeProvider.System);
}
