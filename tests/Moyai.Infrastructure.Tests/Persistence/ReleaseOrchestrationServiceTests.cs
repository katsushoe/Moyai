using Microsoft.Data.Sqlite;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Releases;
using Moyai.Domain.Authentication;
using Moyai.Domain.Releases;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class ReleaseOrchestrationServiceTests
{
    [Fact]
    public async Task PublishSuccessPersistsReleasedAndIsIdempotent()
    {
        await using var fixture = new Fixture(true);
        (ReleaseOrchestrationService service, Release ready) = await fixture.CreateReadyAsync();

        ReleasePublishResult first = await service.PublishAsync("Moyai", ready.Version, ready.Revision, "agent", "test");
        ReleasePublishResult second = await service.PublishAsync("Moyai", ready.Version, first.Release.Revision, "agent", "test");

        Assert.Equal(ReleaseStatus.Released, first.Release.Status);
        Assert.NotNull(first.Release.ReleasedAt);
        Assert.True(second.AlreadyCompleted);
        Assert.Equal(2, fixture.Provider.CallCount);
    }

    [Fact]
    public async Task PublishPassesReleaseNotesAndArtifactPathToProvider()
    {
        await using var fixture = new Fixture(true);
        (ReleaseOrchestrationService service, Release ready) = await fixture.CreateReadyAsync(withArtifact: true);

        await service.PublishAsync("Moyai", ready.Version, ready.Revision, "agent", "test");

        Assert.Equal("artifact.msi", fixture.Provider.LastRequest?.ArtifactPath);
        Assert.Equal("release notes", fixture.Provider.LastRequest?.Notes);
        Assert.Equal(LifecycleAction.ReleasePublish, fixture.Provider.LastRequest?.Action);
    }

    [Fact]
    public async Task PublishFailurePersistsFailedAndAllowsRetry()
    {
        await using var fixture = new Fixture(false);
        (ReleaseOrchestrationService service, Release ready) = await fixture.CreateReadyAsync();

        ReleasePublishResult failed = await service.PublishAsync("Moyai", ready.Version, ready.Revision, "agent", "test");
        fixture.Provider.Succeeds = true;
        ReleasePublishResult retried = await service.RetryAsync("Moyai", ready.Version, failed.Release.Revision, "agent", "test");

        Assert.Equal(ReleaseStatus.Failed, failed.Release.Status);
        Assert.Equal("provider_failure", failed.ProviderResult?.ErrorCode);
        Assert.Equal(ReleaseStatus.Released, retried.Release.Status);
        Assert.Equal(4, fixture.Provider.CallCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"moyai-release-orchestration-{Guid.NewGuid():N}.db");
        private SqliteDatabaseOptions? _options;

        public Fixture(bool succeeds) => Provider = new RecordingProvider(succeeds);
        public RecordingProvider Provider { get; }

        public async Task<(ReleaseOrchestrationService Service, Release Ready)> CreateReadyAsync(bool withArtifact = false)
        {
            _options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(_options).InitializeAsync();
            var projects = new SqliteProjectRepository(_options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "agent", "test"));
            var tokens = new SqliteServiceTokenRepository(_options);
            await tokens.AddAsync(ServiceToken.Issue("githubbie", ["release.write"], DateTimeOffset.UtcNow.AddHours(1), TimeProvider.System));
            var repository = new SqliteReleaseRepository(_options);
            var releaseService = new ReleaseService(projects, repository, TimeProvider.System);
            Release release = await releaseService.CreateAsync(new CreateReleaseCommand("Moyai", "1.0.0", ReleaseChannel.Stable, "release notes", "agent", "test"));
            release = await releaseService.TransitionAsync(new TransitionReleaseCommand("Moyai", release.Version, ReleaseStatus.Planned, release.Revision, "agent", "test"));
            release = await releaseService.TransitionAsync(new TransitionReleaseCommand("Moyai", release.Version, ReleaseStatus.Preparing, release.Revision, "agent", "test"));
            release = await releaseService.TransitionAsync(new TransitionReleaseCommand("Moyai", release.Version, ReleaseStatus.Ready, release.Revision, "agent", "test"));
            var lifecycle = new LifecycleService(projects, tokens, [Provider], new SqliteLifecycleEventWriter(_options, TimeProvider.System), TimeProvider.System);
            var content = new ReleaseContentService(projects, new SqliteWorkItemRepository(_options), repository, new SqliteReleaseContentRepository(_options), TimeProvider.System);
            if (withArtifact) await content.AddArtifactAsync(new AddReleaseArtifactCommand("Moyai", release.Version, null, "installer", "installer", "windows", "x64", "artifact.msi", "artifact.msi", null, 1, new string('a', 64), null, null, "agent", "test"));
            return (new ReleaseOrchestrationService(releaseService, content, lifecycle), release);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProvider(bool succeeds) : ILifecycleProvider
    {
        public string Name => "githubbie";
        public bool Succeeds { get; set; } = succeeds;
        public int CallCount { get; private set; }
        public LifecycleRequest? LastRequest { get; private set; }

        public Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (request.Action == LifecycleAction.ReleaseCreate) return Task.FromResult(new LifecycleResult(true, "release_create", "draft", null, null));
            return Task.FromResult(Succeeds
                ? new LifecycleResult(true, "release_publish", "published", null, null)
                : new LifecycleResult(false, "release_publish", null, "provider_failure", "failed"));
        }
    }
}
