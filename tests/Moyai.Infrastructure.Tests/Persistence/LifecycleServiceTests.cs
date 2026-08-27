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

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"moyai-lifecycle-{Guid.NewGuid():N}.db");

        public async Task<(LifecycleService Service, IReadOnlyDictionary<string, RecordingProvider> Providers)> CreateAsync(string releaseScope = "release.write")
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
            return (new LifecycleService(projects, tokens, providers.Values, new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System), providers);
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

}
