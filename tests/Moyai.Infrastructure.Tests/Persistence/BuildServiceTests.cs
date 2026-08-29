using Microsoft.Data.Sqlite;
using Moyai.Application.Builds;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Domain.Builds;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class BuildServiceTests
{
    [Fact]
    public async Task StartFromCleanCommitPersistsFileArtifactHash()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-build-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "artifact.bin"), "artifact");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", root, "install", "https://github.com/example/moyai", null, "fake", "local", "agent", "test"));
            var tokens = new SqliteServiceTokenRepository(options);
            var routing = new ProviderRoutingService(projects, tokens, [new StatusProvider(false)], TimeProvider.System);
            var lifecycle = new LifecycleService(projects, tokens, [new BuildProvider()], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
            var repository = new SqliteBuildRepository(options);
            var service = new BuildService(projects, repository, routing, lifecycle, TimeProvider.System);

            Build build = await service.StartAsync("Moyai", "Release", "agent", "test");
            BuildArtifact artifact = Assert.Single(await service.ListArtifactsAsync("Moyai", build.Id));

            Assert.Equal(BuildStatus.Succeeded, build.Status);
            Assert.Equal("abc123", build.SourceCommit);
            Assert.Equal("file", artifact.ArtifactKind);
            Assert.Equal(64, artifact.Sha256?.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartFromDirtyTreeIsRejectedBeforeBuildProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-dirty-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", root, "install", "https://github.com/example/moyai", null, "fake", "local", "agent", "test"));
            var tokens = new SqliteServiceTokenRepository(options);
            var buildProvider = new BuildProvider();
            var service = new BuildService(projects, new SqliteBuildRepository(options), new ProviderRoutingService(projects, tokens, [new StatusProvider(true)], TimeProvider.System), new LifecycleService(projects, tokens, [buildProvider], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System), TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("Moyai", "Release", "agent", "test"));

            Assert.Equal(0, buildProvider.CallCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class StatusProvider(bool dirty) : IRepositoryProvider
    {
        public string Name => "githubbie";
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryProviderResult(true, "status", $"{{\"head_sha\":\"abc123\",\"dirty\":{dirty.ToString().ToLowerInvariant()}}}", null, null));
    }

    private sealed class BuildProvider : ILifecycleProvider
    {
        public string Name => "fake";
        public int CallCount { get; private set; }
        public Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(new LifecycleResult(true, "build", "{\"artifacts\":[{\"name\":\"artifact\",\"artifact_type\":\"binary\",\"file_path\":\"artifact.bin\"}]}", null, null)); }
    }
}
