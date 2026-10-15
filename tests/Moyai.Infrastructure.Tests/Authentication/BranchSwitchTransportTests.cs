using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class BranchSwitchTransportTests
{
    [Theory]
    [InlineData("github", true)]
    [InlineData("github", false)]
    [InlineData("bitbucket", true)]
    [InlineData("bitbucket", false)]
    public async Task DispatchRequiresCapabilityAndBranchWriteAssertion(string prefix, bool supported)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Provider = prefix == "github" ? "githubie" : "buckettie", Tool = RepositoryProviderContract.ToolName(prefix, RepositoryOperation.BranchSwitch), Scopes = ["repository.branch.write"] };
        using var handler = new Handler(fixture.Validator(context), context, supported);
        var capability = new AssertionCapability(context.Provider, context.Provider, "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new(context.Provider, new Uri("http://localhost/mcp"), prefix), new Factory(handler), fixture.Issuer, capability);
        var request = Request(context);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(supported, result.Ok);
        Assert.Equal(supported ? 1 : 0, handler.SwitchCalls);
        Assert.Equal(supported ? null : "provider_capability_missing", result.ErrorCode);
        if (supported)
        {
            JsonElement arguments = handler.Arguments!.Value;
            Assert.Equal("registered", arguments.GetProperty("repository").GetString());
            Assert.Equal("feature/existing", arguments.GetProperty("branch").GetString());
            Assert.Equal(2, arguments.EnumerateObject().Count());
            Assert.Contains("previous_branch", result.Output!, StringComparison.Ordinal);
            Assert.Contains("current_branch", result.Output!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongScopeOrLegacyNeverContactsProvider(bool legacy)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_branch_switch", Scopes = ["repository.branch.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = ["repository.read"] });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context) with { UseAssertion = !legacy });
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task BuckettieStandaloneModeNeverDispatchesSwitch()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Provider = "buckettie", Tool = "buckettie_branch_switch", Scopes = ["repository.branch.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true) { IntegrationMode = "standalone" };
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler), fixture.Issuer, capability);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context));
        Assert.Equal("provider_integration_mode_mismatch", result.ErrorCode);
        Assert.Equal(0, handler.SwitchCalls);
    }

    [Fact]
    public async Task LostResponseIsUnknownAndOnlyOneSwitchIsSent()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_branch_switch", Scopes = ["repository.branch.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true) { LoseResponse = true };
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(context));
        Assert.False(result.Ok);
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, handler.SwitchCalls);
    }

    [Theory]
    [InlineData("{\"data\":{\"branch_switch\":true}}", true)]
    [InlineData("{\"data\":{\"operations\":{\"branch_switch\":true}}}", true)]
    [InlineData("{\"data\":{\"branch_switch\":false}}", false)]
    [InlineData("{\"data\":{\"branch_switch\":\"true\"}}", false)]
    [InlineData("{\"data\":{\"operations\":{\"branch_switch\":false},\"branch_switch\":true}}", false)]
    [InlineData("{\"data\":{}}", false)]
    [InlineData("{\"data\":null}", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void CapabilityIsExplicitAndFailClosed(string? json, bool supported) => Assert.Equal(supported, RepositoryProviderContract.SupportsBranchSwitch(json));

    [Theory]
    [InlineData("working_tree_dirty")]
    [InlineData("branch_not_found")]
    [InlineData("invalid_ref")]
    [InlineData("branch_switch_invalid_ref")]
    [InlineData("policy_rejected")]
    [InlineData("protected_branch")]
    [InlineData("branch_not_allowed")]
    [InlineData("permission_denied")]
    public void ProviderRejectionsKeepTheirCode(string code)
    {
        using JsonDocument response = JsonDocument.Parse(JsonSerializer.Serialize(new { ok = false, error = new { code, outcome = "not_executed" } }));
        var parsed = RepositoryProviderResponse.Parse(RepositoryOperation.BranchSwitch, new() { StructuredContent = response.RootElement.Clone() });
        Assert.False(parsed.Ok);
        Assert.Equal(code, parsed.ErrorCode);
        Assert.False(parsed.ResultUnknown);
    }

    [Fact]
    public void SameBranchAndBuckettieBeforeAfterDataRemainInOutput()
    {
        using JsonDocument response = JsonDocument.Parse("""{"ok":true,"data":{"branch_switch":{"previous_branch":"develop","current_branch":"develop","switched":false}}}""");
        var parsed = RepositoryProviderResponse.Parse(RepositoryOperation.BranchSwitch, new() { StructuredContent = response.RootElement.Clone() });
        Assert.True(parsed.Ok);
        using JsonDocument output = JsonDocument.Parse(parsed.Output!);
        JsonElement branch = output.RootElement.GetProperty("data").GetProperty("branch_switch");
        Assert.Equal("develop", branch.GetProperty("previous_branch").GetString());
        Assert.Equal("develop", branch.GetProperty("current_branch").GetString());
        Assert.False(branch.GetProperty("switched").GetBoolean());
    }

    private static RepositoryProviderRequest Request(AssertionContext context) => new("Display", "source", "https://github.com/example/repo.git", "non-origin", RepositoryOperation.BranchSwitch, null, null,
        Branch: "feature/existing", BranchSource: "not-forwarded", ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered");

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(IAssertionValidator validator, AssertionContext expected, bool supported) : HttpMessageHandler
    {
        public string IntegrationMode { get; init; } = "moyai";
        public bool LoseResponse { get; init; }
        public int Requests { get; private set; }
        public int SwitchCalls { get; private set; }
        public JsonElement? Arguments { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            object result;
            string? method = root.GetProperty("method").GetString();
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "branch-fixture", version = "1" } };
            else
            {
                Assert.Equal("tools/call", method);
                string? tool = root.GetProperty("params").GetProperty("name").GetString();
                if (tool is "github_provider_capabilities" or "bitbucket_provider_capabilities")
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { branch_switch = supported, authentication = new { integration_mode = IntegrationMode } } } };
                else
                {
                    Assert.Equal(expected.Tool, tool);
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    SwitchCalls++;
                    await validator.ValidateAsync(Assert.IsType<string>(request.Headers.Authorization?.Parameter), expected, cancellationToken);
                    if (LoseResponse) throw new HttpRequestException("Response lost after switching.");
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { previous_branch = "develop", current_branch = "feature/existing" } } };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
