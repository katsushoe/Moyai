using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class LocalBranchCreateTransportTests
{
    [Theory]
    [InlineData("develop", true)]
    [InlineData("v1.0.1.0", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("develop", false)]
    public async Task DispatchUsesDedicatedCapabilityAndSignedThreeArgumentContract(string source, bool supported)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        AssertionContext context = Context(fixture);
        using var handler = new Handler(fixture.Validator(context), context) { Supported = supported };
        var provider = Provider(fixture, context, handler);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context, source));
        Assert.Equal(supported, result.Ok);
        Assert.Equal(supported ? 1 : 0, handler.CreateCalls);
        Assert.Equal(supported ? null : "provider_capability_missing", result.ErrorCode);
        if (!supported) return;
        JsonElement arguments = handler.Arguments!.Value;
        Assert.Equal("registered", arguments.GetProperty("repository").GetString());
        Assert.Equal("feature/local", arguments.GetProperty("branch").GetString());
        Assert.Equal(source, arguments.GetProperty("source").GetString());
        Assert.Equal(3, arguments.EnumerateObject().Count());
        Assert.Contains("head_sha", result.Output!, StringComparison.Ordinal);
        Assert.Contains("current_branch", result.Output!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("repository.read", true)]
    [InlineData("repository.write", true)]
    [InlineData("repository.branch.write", false)]
    public async Task WrongScopeOrLegacyNeverContactsProvider(string scope, bool assertion)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        AssertionContext context = Context(fixture);
        using var handler = new Handler(fixture.Validator(context), context);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = [scope] });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context, "main") with { UseAssertion = assertion });
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task BuckettieWithoutAgreedContractIsNotGuessedOrDispatched()
    {
        await using var fixture = new AssertionFixture();
        AssertionContext context = Context(fixture);
        using var handler = new Handler(fixture.Validator(context), context);
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context, "main"));
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostOrMalformedResponseIsUnknownAndMutationIsSentOnce(bool malformed)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        AssertionContext context = Context(fixture);
        using var handler = new Handler(fixture.Validator(context), context) { LoseResponse = !malformed, Malformed = malformed };
        RepositoryProviderResult result = await Provider(fixture, context, handler).ExecuteAsync(Request(context, "main"));
        Assert.False(result.Ok);
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, handler.CreateCalls);
    }

    [Fact]
    public async Task FailedCapabilityLookupNeverDispatchesMutation()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        AssertionContext context = Context(fixture);
        using var handler = new Handler(fixture.Validator(context), context) { CapabilityFailure = true };
        RepositoryProviderResult result = await Provider(fixture, context, handler).ExecuteAsync(Request(context, "main"));
        Assert.False(result.Ok);
        Assert.Equal("branch_create_local", result.Operation);
        Assert.Equal(0, handler.CreateCalls);
        Assert.False(result.ResultUnknown);
    }

    [Theory]
    [InlineData("{\"data\":{\"local_branch_create\":true}}", true)]
    [InlineData("{\"data\":{\"local_branch_create\":false,\"branch_create\":true}}", false)]
    [InlineData("{\"data\":{\"branch_create\":true}}", false)]
    [InlineData("{\"data\":{\"local_branch_create\":\"true\"}}", false)]
    [InlineData("{\"data\":{\"local_branch_create\":1}}", false)]
    [InlineData("{\"data\":null}", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void CapabilityMustBeExplicitBoolean(string? json, bool expected) =>
        Assert.Equal(expected, RepositoryProviderContract.SupportsLocalBranchCreate(json));

    [Theory]
    [InlineData("working_tree_dirty", "not_executed")]
    [InlineData("unresolved_conflicts", "not_executed")]
    [InlineData("branch_source_not_found", "not_executed")]
    [InlineData("local_branch_already_exists", "not_executed")]
    [InlineData("invalid_ref", "not_executed")]
    [InlineData("repository_not_allowed", "not_executed")]
    [InlineData("auth_scope_denied", "not_executed")]
    [InlineData("provider_specific_failure", "unknown")]
    public void TypedErrorAndOutcomeArePreserved(string code, string outcome)
    {
        string json = JsonSerializer.Serialize(new { ok = false, error = new { code, outcome, retryable = true, correlation_id = "local-create-test" } });
        using JsonDocument document = JsonDocument.Parse(json);
        var parsed = RepositoryProviderResponse.Parse(RepositoryOperation.BranchCreateLocal, new() { StructuredContent = document.RootElement.Clone() });
        Assert.False(parsed.Ok);
        Assert.Equal(code, parsed.ErrorCode);
        Assert.Equal(outcome == "unknown", parsed.ResultUnknown);
        Assert.Equal(json, parsed.ErrorMessage);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"ok\":false}")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":null}}")]
    public void MalformedErrorAfterDispatchRemainsUnknown(string json)
    {
        var result = RepositoryProviderResponse.Parse(RepositoryOperation.BranchCreateLocal, new() { IsError = true, Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = json }] });
        Assert.False(result.Ok);
        Assert.Equal("provider_invalid_response", result.ErrorCode);
        Assert.True(result.ResultUnknown);
    }

    private static AssertionContext Context(AssertionFixture fixture) => fixture.Context with { Tool = "github_local_branch_create", Scopes = ["repository.branch.write"] };
    private static RepositoryProviderRequest Request(AssertionContext context, string source) => new("Display", "source", "https://github.com/example/repo.git", "non-origin", RepositoryOperation.BranchCreateLocal, null, null,
        Branch: "feature/local", BranchSource: source, ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered");
    private static McpRepositoryProvider Provider(AssertionFixture fixture, AssertionContext context, Handler handler) => new(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer,
        new("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() }));

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(IAssertionValidator validator, AssertionContext expected) : HttpMessageHandler
    {
        public bool Supported { get; init; } = true;
        public bool LoseResponse { get; init; }
        public bool Malformed { get; init; }
        public bool CapabilityFailure { get; init; }
        public int Requests { get; private set; }
        public int CreateCalls { get; private set; }
        public JsonElement? Arguments { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "local-create-fixture", version = "1" } };
            else
            {
                Assert.Equal("tools/call", method);
                string? tool = root.GetProperty("params").GetProperty("name").GetString();
                if (tool == "github_provider_capabilities")
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = !CapabilityFailure, data = new { local_branch_create = Supported } } };
                else
                {
                    Assert.Equal("github_local_branch_create", tool);
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    CreateCalls++;
                    await validator.ValidateAsync(Assert.IsType<string>(request.Headers.Authorization?.Parameter), expected, cancellationToken);
                    if (LoseResponse) throw new HttpRequestException("Response lost after local branch creation.");
                    object payload = Malformed ? new { data = "missing-status" } : new { ok = true, data = new { name = "feature/local", source = Arguments.Value.GetProperty("source").GetString(), current_branch = "develop", head_sha = "0123456789abcdef0123456789abcdef01234567" } };
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = payload };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
