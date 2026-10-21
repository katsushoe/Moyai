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

        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.Commit, "message");

        Assert.False(result.Ok);
        Assert.Equal("invalid_service_token", result.ErrorCode);
        Assert.Equal("repository_commit", result.Operation);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsyncFailsClosedWhenWriteScopeIsMissing()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true, scope: "repository.read");

        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.Pull);

        Assert.False(result.Ok);
        Assert.Equal("service_token_scope_missing", result.ErrorCode);
        Assert.Equal("pull", result.Operation);
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
        Assert.Null(request.RemoteName);
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

    [Fact]
    public async Task BranchSwitchUsesProjectIdentityAndMappedRepositoryWithoutMutation()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false, mapped: true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchSwitch, branch: "feature/existing");
        Assert.True(result.Ok);
        RepositoryProviderRequest request = Assert.IsType<RepositoryProviderRequest>(provider.LastRequest);
        Assert.True(request.UseAssertion);
        Assert.NotEqual(Guid.Empty, request.ProjectId);
        Assert.Equal("registered", request.RepositoryId);
        Assert.Equal("feature/existing", request.Branch);
        Assert.Null(request.BranchSource);
        Assert.Null(request.ServiceToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BranchSwitchRejectsMissingBranchBeforeDispatch(string? branch)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.BranchSwitch, branch: branch));
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task BranchSwitchRejectsLegacyEvenWithGenericWriteToken()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchSwitch, branch: "develop");
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task UnknownBranchSwitchOutcomeDoesNotRetryExpiryCode()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false);
        provider.Next = new(false, "branch_switch", null, "auth_assertion_expired", null, true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchSwitch, branch: "develop");
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("develop")]
    [InlineData("v1.0.1.0")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task LocalBranchCreationForwardsExplicitSourceAndProjectIdentity(string source)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false, mapped: true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreateLocal, branch: "feature/local", source: source);
        Assert.True(result.Ok);
        RepositoryProviderRequest request = Assert.IsType<RepositoryProviderRequest>(provider.LastRequest);
        Assert.Equal(RepositoryOperation.BranchCreateLocal, request.Operation);
        Assert.Equal("feature/local", request.Branch);
        Assert.Equal(source, request.BranchSource);
        Assert.Equal("registered", request.RepositoryId);
        Assert.NotEqual(Guid.Empty, request.ProjectId);
        Assert.Equal("https://github.com/example/moyai", request.RepositoryUrl);
        Assert.True(request.UseAssertion);
        Assert.Null(request.ServiceToken);
    }

    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("feature/local", null)]
    [InlineData("feature/local", " ")]
    [InlineData("feature/local", "HEAD")]
    [InlineData("feature/local", "HEAD~1")]
    [InlineData("feature/local", "main^2")]
    [InlineData("feature/local", "refs/remotes/origin/main")]
    [InlineData("feature/local", "-main")]
    [InlineData("feature/local", "part.lock/next")]
    [InlineData("feature/local", "main//next")]
    [InlineData("feature/local", "main/.hidden")]
    [InlineData("bad..branch", "main")]
    [InlineData("HEAD", "main")]
    public async Task LocalBranchCreationRejectsInvalidInputsBeforeDispatch(string? branch, string? source)
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreateLocal, branch: branch, source: source));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task LocalBranchCreationRejectsLegacyWriteToken()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreateLocal, branch: "feature/local", source: "main");
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task LocalBranchCreationUnknownExpiryIsNotRetried()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false);
        provider.Next = new(false, "branch_create_local", null, "auth_assertion_expired", null, true);
        RepositoryProviderResult result = await service.ExecuteAsync("Moyai", RepositoryOperation.BranchCreateLocal, branch: "feature/local", source: "main");
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task PullRequestUsesMappedProjectContextAndDoesNotRetryUnknownResult()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: false, legacy: false, mapped: true);
        provider.Next = new(false, "pull_request_create", null, "auth_assertion_expired", null, true);
        var result = await service.ExecutePullRequestAsync("Moyai", new("docs/proposal", "main", "Title", "", false));
        Assert.Equal("unknown", result.Error!.Outcome);
        Assert.Equal(1, provider.Calls);
        Assert.Equal("registered", provider.LastRequest!.RepositoryId);
        Assert.NotEqual(Guid.Empty, provider.LastRequest.ProjectId);
        Assert.True(provider.LastRequest.UseAssertion);
        Assert.Equal("docs/proposal", provider.LastRequest.PullRequest!.SourceBranch);
        Assert.Null(provider.LastRequest.ServiceToken);
    }

    [Fact]
    public async Task PullRequestRejectsUnknownProjectAndLegacyBeforeDispatch()
    {
        await using var fixture = new RoutingFixture();
        (ProviderRoutingService service, RecordingProvider provider) = await fixture.CreateAsync(issueToken: true);
        var input = new PullRequestInput("docs/proposal", "main", "Title", "", false);
        var missing = await service.ExecutePullRequestAsync("Unknown", input);
        Assert.Equal("project_not_found", missing.Error!.Code);
        var legacy = await service.ExecutePullRequestAsync("Moyai", input);
        Assert.Equal("auth_scope_denied", legacy.Error!.Code);
        Assert.Equal(0, provider.Calls);
    }

    private sealed class RoutingFixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"moyai-routing-{Guid.NewGuid():N}.db");

        public async Task<(ProviderRoutingService Service, RecordingProvider Provider)> CreateAsync(bool issueToken, string scope = "repository.write", bool legacy = true, bool mapped = false)
        {
            string connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
            var options = new SqliteDatabaseOptions(connectionString);
            await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
            var projectRepository = new SqliteProjectRepository(options);
            var projectService = new ProjectService(projectRepository, TimeProvider.System);
            await projectService.CreateAsync(new CreateProjectCommand("Moyai", "source", "install", "https://github.com/example/moyai", null, "dotnet", "local", "test", "routing"));
            if (mapped) await projectService.SetProviderRepositoryAsync("Moyai", 1, "registered", false, "test", "routing");
            var tokenRepository = new SqliteServiceTokenRepository(options);
            if (issueToken)
            {
                ServiceToken token = ServiceToken.Issue("githubbie", [scope], DateTimeOffset.UtcNow.AddMinutes(10), TimeProvider.System);
                await tokenRepository.AddAsync(token);
            }
            var provider = new RecordingProvider();
            return (new ProviderRoutingService(projectRepository, tokenRepository, [provider], TimeProvider.System,
                legacy ? new RepositoryAuthentication("legacy", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)) : null), provider);
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
        public RepositoryProviderResult? Next { get; set; }
        public int Calls { get; private set; }

        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Calls++;
            return Task.FromResult(Next ?? new RepositoryProviderResult(true, request.Operation.ToString(), "ok", null, null));
        }
    }
}
