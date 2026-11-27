using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class PullRequestTransportTests
{
    [Theory]
    [InlineData("github", "success")]
    [InlineData("bitbucket", "success")]
    [InlineData("github", "unsupported")]
    [InlineData("bitbucket", "unsupported")]
    [InlineData("github", "wrong-scope")]
    [InlineData("bitbucket", "wrong-scope")]
    [InlineData("github", "legacy")]
    [InlineData("bitbucket", "standalone")]
    [InlineData("github", "policy")]
    [InlineData("bitbucket", "policy")]
    [InlineData("github", "response-lost")]
    [InlineData("bitbucket", "response-lost")]
    public async Task SignedDispatchPreservesRouteAndFailsClosed(string prefix, string scenario)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        string providerName = prefix == "github" ? "githubie" : "buckettie";
        string scope = RepositoryAssertionPolicy.Scope(RepositoryOperation.PullRequestCreate, providerName);
        var context = fixture.Context with { Provider = providerName, Tool = prefix + "_pr_create", Scopes = [scope] };
        using var handler = new Handler(fixture.Validator(context), context, prefix, scenario);
        var capability = new AssertionCapability(providerName, providerName, "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = [scenario == "wrong-scope" ? "repository.push" : scope] });
        var provider = new McpRepositoryProvider(new(providerName, new Uri("http://localhost/mcp"), prefix), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "unused", "https://github.com/example/repo.git", "non-origin", RepositoryOperation.PullRequestCreate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: scenario != "legacy", ProviderRepositoryId: "registered",
            PullRequest: new("docs/proposal", "release", "日本語", "", false));

        RepositoryProviderResult raw = await provider.ExecuteAsync(request);
        PullRequestResult result = PullRequestResult.Parse("Display", providerName, raw);

        bool sent = scenario is "success" or "policy" or "response-lost";
        Assert.Equal(sent ? 1 : 0, handler.CreateCalls);
        Assert.Equal(scenario == "success", result.Ok);
        if (scenario is "wrong-scope" or "legacy") Assert.Equal(0, handler.Requests);
        if (scenario == "unsupported") Assert.Equal("provider_capability_missing", result.Error!.Code);
        if (scenario == "wrong-scope" || scenario == "legacy") Assert.Equal("auth_scope_denied", result.Error!.Code);
        if (scenario == "standalone") Assert.Equal("provider_integration_mode_mismatch", result.Error!.Code);
        if (scenario == "policy") Assert.Equal("pull_request_route_not_allowed", result.Error!.Code);
        if (scenario == "response-lost") Assert.Equal("unknown", result.Error!.Outcome);
        if (scenario != "success") return;
        Assert.Equal(42, result.PullRequest!.Id);
        Assert.Equal("docs/proposal", result.PullRequest.SourceBranch);
        Assert.Equal("release", result.PullRequest.DestinationBranch);
        JsonElement args = handler.Arguments!.Value;
        Assert.Equal("registered", args.GetProperty("repository").GetString());
        Assert.Equal("docs/proposal", args.GetProperty(prefix == "github" ? "source_branch" : "sourceBranch").GetString());
        Assert.Equal("release", args.GetProperty(prefix == "github" ? "destination_branch" : "destinationBranch").GetString());
        Assert.Equal("日本語", args.GetProperty("title").GetString());
        Assert.Equal("", args.GetProperty("description").GetString());
        Assert.False(args.GetProperty("draft").GetBoolean());
        Assert.Equal(6, args.EnumerateObject().Count());
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(IAssertionValidator validator, AssertionContext expected, string prefix, string scenario) : HttpMessageHandler
    {
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
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "pr-fixture", version = "1" } };
            else
            {
                Assert.Equal("tools/call", method);
                string? tool = root.GetProperty("params").GetProperty("name").GetString();
                if (tool == prefix + "_provider_capabilities")
                {
                    object data = prefix == "github" ? new { pr_create = scenario != "unsupported" }
                        : new { operations = new { pr_create = scenario != "unsupported" }, authentication = new { integration_mode = scenario == "standalone" ? "standalone" : "moyai" } };
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data } };
                }
                else
                {
                    Assert.Equal(expected.Tool, tool);
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    CreateCalls++;
                    await validator.ValidateAsync(Assert.IsType<string>(request.Headers.Authorization?.Parameter), expected, cancellationToken);
                    if (scenario == "response-lost") throw new HttpRequestException("Response lost after PR creation.");
                    object data = prefix == "github"
                        ? new { number = 42, title = "日本語", body = "", source = "docs/proposal", destination = "release", state = "open", url = "https://example.invalid/pr/42" }
                        : new { id = 42, title = "日本語", description = "", source_branch = "docs/proposal", destination_branch = "release", draft = false, state = "OPEN", url = "https://example.invalid/pr/42" };
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = scenario != "policy", data, error = scenario == "policy" ? new { code = "pull_request_route_not_allowed", outcome = "not_executed" } : null } };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
