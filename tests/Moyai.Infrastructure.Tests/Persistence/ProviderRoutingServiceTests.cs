using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Domain.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class ProviderRoutingServiceTests
{
    [Fact]
    public async Task ExecuteAsyncRoutesMutationWithAudienceToken()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);

        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.Push);

        Assert.True(result.Ok);
        Assert.Equal("githubbie", provider.Name);
        Assert.NotNull(provider.LastRequest?.ServiceToken);
        Assert.Equal(RepositoryOperation.Push, provider.LastRequest?.Operation);
    }

    [Fact]
    public async Task ExecuteAsyncAllowsReadWithoutServiceToken()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false);

        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.Status);

        Assert.True(result.Ok);
        Assert.Null(provider.LastRequest?.ServiceToken);
    }

    [Fact]
    public async Task ExecuteAsyncFailsClosedWhenMutationTokenIsMissing()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false);

        ProviderRoutingException exception = await Assert.ThrowsAsync<ProviderRoutingException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.Commit, "message"));

        Assert.Equal("invalid_service_token", exception.Code);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsyncFailsClosedWhenWriteScopeIsMissing()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true, scope: "repository.read");

        ProviderRoutingException exception = await Assert.ThrowsAsync<ProviderRoutingException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.Pull));

        Assert.Equal("service_token_scope_missing", exception.Code);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsyncRoutesBranchMutationWithRepositoryContext()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);

        await service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreate, branch: "feature/test", source: "main");

        RepositoryProviderRequest request = Assert.IsType<RepositoryProviderRequest>(provider.LastRequest);
        Assert.Equal("feature/test", request.Branch);
        Assert.Equal("main", request.BranchSource);
        Assert.Equal("source", request.SourcePath);
        Assert.Equal("https://github.com/example/moyai", request.RepositoryUrl);
        Assert.Equal("origin", request.RemoteName);
        Assert.NotNull(request.ServiceToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HEAD~1")]
    [InlineData("main^2")]
    [InlineData("refs/heads/main..develop")]
    public async Task ExecuteAsyncRejectsInvalidBranchSource(string? source)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreate, branch: "feature/test", source: source));

        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsyncRoutesFullCommitShaBranchSource()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);
        const string source = "0123456789abcdef0123456789abcdef01234567";

        await service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreate, branch: "feature/test", source: source);

        Assert.Equal(source, provider.LastRequest?.BranchSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAsyncRejectsTagCreateWithoutSource(string? source)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.TagCreate, tag: "v1.0.0", source: source));

        Assert.Null(provider.LastRequest);
    }

    [Theory]
    [InlineData("develop")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task ExecuteAsyncRoutesTagCreateSource(string source)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);

        await service.ExecuteAsync("Moyai", RepositoryOperation.TagCreate, tag: "v1.0.0", source: source);

        Assert.Equal(source, provider.LastRequest?.BranchSource);
    }

    private sealed class RoutingFixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"moyai-routing-{Guid.NewGuid():N}.db");

        public async Task<(ProviderRoutingService Service, RecordingProvider Provider)> CreateAsync(bool issueToken, string scope = "repository.write")
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projectRepository = new SqliteProjectRepository(options);
            var projectService = new ProjectService(projectRepository, TimeProvider.System);
            await projectService.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "test", "routing"));
            var tokenRepository = new SqliteServiceTokenRepository(options);
            if (issueToken)
            {
                ServiceToken token = ServiceToken.Issue("githubbie", [scope], DateTimeOffset.UtcNow.AddMinutes(10), TimeProvider.System);
                await tokenRepository.AddAsync(token);
            }
            var provider = new RecordingProvider();
            return (new ProviderRoutingService(projectRepository, tokenRepository, [provider], TimeProvider.System), provider);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProvider : IRepositoryProvider
    {
        public string Name => "githubbie";
        public RepositoryProviderRequest? LastRequest { get; private set; }

        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new RepositoryProviderResult(true, request.Operation.ToString(), "ok", null, null));
        }
    }
}
