using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class AssertionTransportTests
{
    [Theory]
    [InlineData("githubbie", "githubie", "github")]
    [InlineData("buckettie", "buckettie", "bitbucket")]
    public async Task McpCallUsesCanonicalAssertionProviderOnlyForToolAndNeverExposesIt(string routingProvider, string assertionProvider, string prefix)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        AssertionContext context = f.Context with { Provider = assertionProvider, Tool = prefix + "_push" };
        using var handler = new ValidatingHandler(f.Validator(context), context);
        var factory = new TestClientFactory(handler);
        var capability = new AssertionCapability(assertionProvider, assertionProvider, "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = ["repository.push"] });
        var adapter = new McpRepositoryProvider(new McpRepositoryProviderOptions(routingProvider, new Uri("http://127.0.0.1:54321/mcp"), prefix), factory,
            f.Issuer, capability, new SqliteAssertionAudit(f.Database, f.Clock));
        var request = new RepositoryProviderRequest("Test", "unused", "https://github.com/example/repo.git", "origin", RepositoryOperation.Push, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true);
        RepositoryProviderResult result = await adapter.ExecuteAsync(request);
        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(1, handler.Executions);
        Assert.True(handler.BootstrapWithoutAssertion);
        Assert.NotNull(handler.Assertion);
        Assert.DoesNotContain(handler.Assertion!, result.Output!, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Assertion!, JsonSerializer.Serialize(request), StringComparison.Ordinal);
        await using var connection = new SqliteConnection(f.Database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT result_code FROM assertion_audit ORDER BY rowid;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("issued", reader.GetString(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("accepted", reader.GetString(0));
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData(null)]
    public async Task BuckettieStateChangeIsNotDelegatedOutsideMoyaiIntegrationMode(string? integrationMode)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        AssertionContext context = f.Context with { Provider = "buckettie", Tool = "bitbucket_push" };
        using var handler = new ValidatingHandler(f.Validator(context), context, integrationMode);
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = ["repository.push"] });
        var adapter = new McpRepositoryProvider(new McpRepositoryProviderOptions("buckettie", new Uri("http://127.0.0.1:54321/mcp"), "bitbucket"), new TestClientFactory(handler),
            f.Issuer, capability, new SqliteAssertionAudit(f.Database, f.Clock));
        var request = new RepositoryProviderRequest("Test", "unused", "https://bitbucket.org/example/repo.git", "origin", RepositoryOperation.Push, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true);
        RepositoryProviderResult result = await adapter.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.Equal("provider_integration_mode_mismatch", result.ErrorCode);
        Assert.Equal(1, handler.CapabilityChecks);
        Assert.Equal(0, handler.Executions);
        Assert.Null(handler.Assertion);
    }

    [Theory]
    [InlineData("bitbucket", RepositoryOperation.Status, false)]
    [InlineData("bitbucket", RepositoryOperation.BranchList, false)]
    [InlineData("bitbucket", RepositoryOperation.Push, true)]
    [InlineData("bitbucket", RepositoryOperation.Pull, true)]
    [InlineData("bitbucket", RepositoryOperation.TagCreate, true)]
    [InlineData("github", RepositoryOperation.Push, false)]
    public void IntegrationModeIsCheckedOnlyForBuckettieStateChanges(string prefix, RepositoryOperation operation, bool expected) =>
        Assert.Equal(expected, McpRepositoryProvider.RequiresIntegrationModeCheck(prefix, operation));

    [Theory]
    [InlineData("auth_assertion_expired", 2)]
    [InlineData("auth_replay_detected", 1)]
    [InlineData("auth_key_revoked", 1)]
    [InlineData("provider_unavailable", 1)]
    [InlineData("provider_policy_rejected", 1)]
    public async Task RoutingRetriesOnlyExpiredAssertionAtMostOnce(string error, int calls)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        var projects = new SqliteProjectRepository(f.Database);
        var projectService = new ProjectService(projects, f.Clock);
        await projectService.CreateAsync(new CreateProjectCommand("Test", "unused", null, "https://github.com/example/repo", "github", "dotnet", "local", "test", "test"));
        var provider = new FailingProvider(error);
        var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(f.Database), [provider], f.Clock);
        RepositoryProviderResult result = await routing.ExecuteAsync("Test", RepositoryOperation.Push);
        Assert.False(result.Ok);
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal(calls, provider.Calls);
        Assert.True(provider.LastRequest!.UseAssertion);
        Assert.NotEqual(Guid.Empty, provider.LastRequest.ProjectId);
        Assert.Null(provider.LastRequest.ServiceToken);
    }

    [Fact]
    public void LegacyMigrationExpiresAndCannotBeIndefinite()
    {
        var clock = new TestClock();
        var policy = new RepositoryAuthentication("legacy", clock.Now, clock.Now.AddDays(7));
        Assert.True(policy.UseLegacy(clock));
        clock.Now = clock.Now.AddDays(7);
        Assert.Throws<ProviderAuthenticationException>(() => policy.UseLegacy(clock));
        Assert.Throws<ProviderAuthenticationException>(() => new RepositoryAuthentication("legacy").UseLegacy(clock));
        Assert.Throws<ProviderAuthenticationException>(() => new RepositoryAuthentication("legacy", clock.Now, clock.Now.AddDays(8)).UseLegacy(clock));
        Assert.False(new RepositoryAuthentication().UseLegacy(clock));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "github.com/owner/repo")]
    public void RepositoryNormalizationStripsTransportOnly(string input, string expected) => Assert.Equal(expected, RepositoryAssertionPolicy.NormalizeRepository(input));

    [Theory]
    [InlineData("https://token@github.com/owner/repo")]
    [InlineData("file:///tmp/repo")]
    [InlineData("https://github.com/owner/repo?token=secret")]
    public void RepositoryNormalizationRejectsCredentials(string input) => Assert.Throws<ProviderAuthenticationException>(() => RepositoryAssertionPolicy.NormalizeRepository(input));

    private sealed class TestClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class FailingProvider(string error) : IRepositoryProvider
    {
        public string Name => "githubbie";
        public int Calls { get; private set; }
        public RepositoryProviderRequest? LastRequest { get; private set; }
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new RepositoryProviderResult(false, request.Operation.ContractName(), null, error, error));
        }
    }
    private sealed class ValidatingHandler(IAssertionValidator validator, AssertionContext context, string? integrationMode = "moyai") : HttpMessageHandler
    {
        public int Executions { get; private set; }
        public int CapabilityChecks { get; private set; }
        public bool BootstrapWithoutAssertion { get; private set; }
        public string? Assertion { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "tools/call" && root.GetProperty("params").GetProperty("name").GetString() == "bitbucket_provider_capabilities")
            {
                Assert.Null(request.Headers.Authorization);
                CapabilityChecks++;
                object authentication = integrationMode is null ? new { provider_id = "buckettie" } : new { provider_id = "buckettie", integration_mode = integrationMode };
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { provider = "bitbucket", authentication } } };
            }
            else if (method == "tools/call")
            {
                Assertion = request.Headers.Authorization?.Parameter;
                Assert.NotNull(Assertion);
                Assert.DoesNotContain(Assertion!, body, StringComparison.Ordinal);
                Assert.Equal(context.OperationId, request.Headers.GetValues("X-Moyai-Operation-Id").Single());
                await validator.ValidateAsync(Assertion!, context, cancellationToken);
                Executions++;
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, echo = Assertion } };
            }
            else if (method == "server/discover")
            {
                Assert.Null(request.Headers.Authorization);
                BootstrapWithoutAssertion = true;
                result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            }
            else
            {
                Assert.Null(request.Headers.Authorization);
                BootstrapWithoutAssertion = true;
                Assert.Equal("initialize", method);
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "isolated-provider", version = "1.0" } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
