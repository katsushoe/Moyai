using Microsoft.Data.Sqlite;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Domain.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class LifecycleServiceTests
{
    [Fact]
    public async Task ExecuteAsyncRoutesBuildReleaseAndDeployToConfiguredProviders()
    {
        await using var fixture = new LifecycleFixture();
        (LifecycleService service, IReadOnlyDictionary<string, RecordingProvider> providers) = await fixture.CreateAsync();

        await service.ExecuteAsync("Moyai", LifecycleAction.Build, "test", "lifecycle");
        await service.ExecuteAsync("Moyai", LifecycleAction.ReleaseCreate, "test", "lifecycle", "1.0.0", notes: "notes");
        await service.ExecuteAsync("Moyai", LifecycleAction.Deploy, "test", "lifecycle", "1.0.0", "artifact.msi");

        Assert.Null(providers["dotnet"].LastRequest?.ServiceToken);
        Assert.NotNull(providers["githubbie"].LastRequest?.ServiceToken);
        Assert.Equal("release.write", providers["githubbie"].IssuedScope);
        Assert.NotNull(providers["local"].LastRequest?.ServiceToken);
        Assert.Equal(LifecycleAction.Deploy, providers["local"].LastRequest?.Action);
        Assert.Equal(3L, await fixture.LifecycleEventCountAsync());
    }

    [Fact]
    public async Task ExecuteAsyncFailsClosedWhenReleaseScopeIsMissing()
    {
        await using var fixture = new LifecycleFixture();
        (LifecycleService service, IReadOnlyDictionary<string, RecordingProvider> providers) = await fixture.CreateAsync(releaseScope: "repository.write");

        ProviderRoutingException exception = await Assert.ThrowsAsync<ProviderRoutingException>(() => service.ExecuteAsync("Moyai", LifecycleAction.ReleasePublish, "test", "lifecycle", "1.0.0"));

        Assert.Equal("service_token_scope_missing", exception.Code);
        Assert.Null(providers["githubbie"].LastRequest);
    }

    [Fact]
    public async Task LegacyReleaseOutsideMigrationWindowIsRejectedWithoutToken()
    {
        await using var fixture = new LifecycleFixture();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (LifecycleService service, IReadOnlyDictionary<string, RecordingProvider> providers) = await fixture.CreateAsync(
            authentication: new Moyai.Application.Authentication.RepositoryAuthentication("legacy", now.AddDays(-8), now.AddDays(-1)));

        LifecycleResult result = await service.ExecuteAsync("Moyai", LifecycleAction.ReleaseCreate, "test", "lifecycle", "1.0.0");

        Assert.False(result.Ok);
        Assert.Equal("authentication_unavailable", result.ErrorCode);
        Assert.Null(providers["githubbie"].LastRequest);
    }

    [Fact]
    public async Task LegacyReleaseInsideMigrationWindowUsesServiceToken()
    {
        await using var fixture = new LifecycleFixture();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (LifecycleService service, IReadOnlyDictionary<string, RecordingProvider> providers) = await fixture.CreateAsync(
            authentication: new Moyai.Application.Authentication.RepositoryAuthentication("legacy", now.AddDays(-1), now.AddDays(1)));

        LifecycleResult result = await service.ExecuteAsync("Moyai", LifecycleAction.ReleaseCreate, "test", "lifecycle", "1.0.0");

        Assert.True(result.Ok);
        Assert.NotNull(providers["githubbie"].LastRequest?.ServiceToken);
    }

    [Fact]
    public async Task AssertionReleaseProviderDoesNotRequireLegacyServiceToken()
    {
        await using var fixture = new LifecycleFixture();
        (LifecycleService service, AssertionProvider provider) = await fixture.CreateAssertionAsync();

        LifecycleResult result = await service.ExecuteAsync("Moyai", LifecycleAction.ReleaseCreate, "test", "lifecycle", "1.0.0", notes: "notes");

        Assert.True(result.Ok);
        Assert.NotNull(provider.LastRequest);
        Assert.Null(provider.LastRequest.ServiceToken);
        Assert.NotEqual(Guid.Empty, provider.LastRequest.ProjectId);
        Assert.Equal("https://github.com/example/moyai", provider.LastRequest.RepositoryUrl);
    }

    [Fact]
    public async Task ServerDeployUsesCanonicalKelpieProviderWithoutServiceToken()
    {
        await using var fixture = new LifecycleFixture();
        (LifecycleService service, RecordingProvider provider, Guid projectId) = await fixture.CreateServerAsync();
        Guid deploymentId = Guid.NewGuid();

        await service.ExecuteAsync(
            "Moyai",
            LifecycleAction.Deploy,
            "test",
            "lifecycle",
            "1.0.0",
            "artifact.zip",
            deploymentId: deploymentId,
            kelpieTarget: "target-01",
            destinationPath: "/srv/app/artifact.zip",
            artifactSha256: new string('a', 64));

        Assert.NotNull(provider.LastRequest);
        Assert.Null(provider.LastRequest.ServiceToken);
        Assert.Equal(projectId, provider.LastRequest.ProjectId);
        Assert.Equal(deploymentId, provider.LastRequest.DeploymentId);
        Assert.Equal("target-01", provider.LastRequest.KelpieTarget);
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"moyai-lifecycle-{Guid.NewGuid():N}.db");

        public async Task<(LifecycleService Service, IReadOnlyDictionary<string, RecordingProvider> Providers)> CreateAsync(string releaseScope = "release.write",
            Moyai.Application.Authentication.RepositoryAuthentication? authentication = null)
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "test", "lifecycle"));
            var tokens = new SqliteServiceTokenRepository(options);
            await tokens.AddAsync(ServiceToken.Issue("githubbie", [releaseScope], DateTimeOffset.UtcNow.AddHours(1), TimeProvider.System));
            await tokens.AddAsync(ServiceToken.Issue("local", ["deploy.write"], DateTimeOffset.UtcNow.AddHours(1), TimeProvider.System));
            var providers = new Dictionary<string, RecordingProvider>(StringComparer.Ordinal)
            {
                ["dotnet"] = new("dotnet", null),
                ["githubbie"] = new("githubbie", releaseScope),
                ["local"] = new("local", "deploy.write"),
            };
            return (new LifecycleService(projects, tokens, providers.Values, new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System, authentication), providers);
        }

        public async Task<(LifecycleService Service, AssertionProvider Provider)> CreateAssertionAsync()
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            await new ProjectService(projects, TimeProvider.System).CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "test", "lifecycle"));
            var provider = new AssertionProvider();
            var service = new LifecycleService(projects, new SqliteServiceTokenRepository(options), [provider], new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
            return (service, provider);
        }

        public async Task<(LifecycleService Service, RecordingProvider Provider, Guid ProjectId)> CreateServerAsync()
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new SqliteProjectRepository(options);
            var project = await new ProjectService(projects, TimeProvider.System).CreateAsync(
                new CreateProjectCommand("Moyai", "source", null, "", null, "", "server", "test", "lifecycle"));
            var provider = new RecordingProvider("kelpiessh", null);
            var service = new LifecycleService(
                projects,
                new SqliteServiceTokenRepository(options),
                [provider],
                new SqliteLifecycleEventWriter(options, TimeProvider.System),
                TimeProvider.System);
            return (service, provider, project.Id);
        }

        public async Task<long> LifecycleEventCountAsync()
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM events WHERE entity_type='lifecycle';";
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProvider(string name, string? issuedScope) : ILifecycleProvider
    {
        public string Name { get; } = name;
        public string? IssuedScope { get; } = issuedScope;
        public LifecycleRequest? LastRequest { get; private set; }

        public Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new LifecycleResult(true, request.Action.ToString(), "ok", null, null));
        }
    }

    private sealed class AssertionProvider : ILifecycleProvider
    {
        public string Name => "githubbie";
        public LifecycleRequest? LastRequest { get; private set; }

        public bool UsesAssertion(LifecycleAction action) => true;

        public Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new LifecycleResult(true, request.Action.ToString(), "ok", null, null));
        }
    }
}
