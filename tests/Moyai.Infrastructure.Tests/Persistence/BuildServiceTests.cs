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
    [Theory]
    [InlineData("{\"head_sha\":\"abc123\",\"dirty\":false}")]
    [InlineData("{\"commit\":\"abc123\",\"is_dirty\":false}")]
    [InlineData("{\"head\":\"abc123\",\"dirty\":false}")]
    [InlineData("{\"ok\":true,\"data\":{\"local_head\":\"abc123\",\"working_tree_clean\":true}}")]
    public async Task StartFromCleanCommitPersistsFileArtifactHash(string statusOutput)
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
            var routing = new ProviderRoutingService(projects, tokens, [new JsonStatusProvider(statusOutput)], TimeProvider.System);
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

    [Theory]
    [InlineData("{\"head_sha\":\"abc123\",\"dirty\":true}", "A standard build requires a clean working tree.")]
    [InlineData("{\"ok\":true,\"data\":{\"local_head\":\"abc123\",\"working_tree_clean\":false}}", "A standard build requires a clean working tree.")]
    [InlineData("{\"ok\":true,\"data\":{\"head_sha\":\"abc123\",\"working_tree_clean\":false}}", "A standard build requires a clean working tree.")]
    [InlineData("{\"ok\":true,\"data\":{\"local_head\":null,\"working_tree_clean\":true}}", "Repository status did not return a source commit.")]
    [InlineData("{\"ok\":true,\"data\":{\"local_head\":\"\",\"working_tree_clean\":true}}", "Repository status did not return a source commit.")]
    [InlineData("{\"ok\":true,\"data\":{\"working_tree_clean\":true}}", "Repository status did not return a source commit.")]
    public async Task StartFromInvalidStatusIsRejectedBeforeBuildProvider(string statusOutput, string expectedMessage)
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
            var service = new BuildService(projects, new SqliteBuildRepository(options), new ProviderRoutingService(projects, tokens, [new JsonStatusProvider(statusOutput)], TimeProvider.System), new LifecycleService(projects, tokens, [buildProvider], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System), TimeProvider.System);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("Moyai", "Release", "agent", "test"));

            Assert.Equal(expectedMessage, exception.Message);
            Assert.Equal(0, buildProvider.CallCount);
            Assert.Empty(await service.ListAsync("Moyai"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartWithDirectoryArtifactPersistsDeterministicManifestHash()
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-directory-build-{Guid.NewGuid():N}");
        string artifactDirectory = Path.Combine(root, "publish");
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "app.dll"), "application");
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "nested", "settings.json"), "{}");
        try
        {
            var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "moyai.db"), Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", root, "install", "https://github.com/example/moyai", null, "fake", "local", "agent", "test"));
            var tokens = new SqliteServiceTokenRepository(options);
            var routing = new ProviderRoutingService(projects, tokens, [new StatusProvider(false)], TimeProvider.System);
            var lifecycle = new LifecycleService(projects, tokens, [new BuildProvider("publish")], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
            var service = new BuildService(projects, new SqliteBuildRepository(options), routing, lifecycle, TimeProvider.System);

            Build firstBuild = await service.StartAsync("Moyai", "Release", "agent", "test");
            BuildArtifact firstArtifact = Assert.Single(await service.ListArtifactsAsync("Moyai", firstBuild.Id));
            Build secondBuild = await service.StartAsync("Moyai", "Release", "agent", "test");
            BuildArtifact secondArtifact = Assert.Single(await service.ListArtifactsAsync("Moyai", secondBuild.Id));

            Assert.Equal("directory", firstArtifact.ArtifactKind);
            Assert.Null(firstArtifact.Sha256);
            Assert.Equal(64, firstArtifact.ManifestSha256?.Length);
            Assert.Equal(firstArtifact.ManifestSha256, secondArtifact.ManifestSha256);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class JsonStatusProvider(string output) : IRepositoryProvider
    {
        public string Name => "githubbie";
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryProviderResult(true, "status", output, null, null));
    }

    private sealed class StatusProvider(bool dirty) : IRepositoryProvider
    {
        public string Name => "githubbie";
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryProviderResult(true, "status", $"{{\"head_sha\":\"abc123\",\"dirty\":{dirty.ToString().ToLowerInvariant()}}}", null, null));
    }

    private sealed class BuildProvider(string artifactPath = "artifact.bin") : ILifecycleProvider
    {
        public string Name => "fake";
        public int CallCount { get; private set; }
        public Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(new LifecycleResult(true, "build", $"{{\"artifacts\":[{{\"name\":\"artifact\",\"artifact_type\":\"binary\",\"file_path\":\"{artifactPath}\"}}]}}", null, null)); }
    }
}
