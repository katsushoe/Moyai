using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class PullRequestMergeTransportTests
{
    private static readonly string Source = new('a', 40);
    private static readonly string Merged = new('c', 40);
    private static readonly string Credential = Convert.ToBase64String(new byte[32]);
    private const string Generation = "00000000000000000000000000000001";

    [Theory]
    [InlineData(false, "success", 1, true, false)]
    [InlineData(true, "success", 1, true, false)]
    [InlineData(false, "already-merged", 0, true, false)]
    [InlineData(true, "already-merged", 0, true, false)]
    [InlineData(false, "source-mismatch", 0, false, false)]
    [InlineData(true, "source-mismatch", 0, false, false)]
    [InlineData(false, "draft", 0, false, false)]
    [InlineData(true, "draft-missing", 0, false, false)]
    [InlineData(false, "closed", 0, false, false)]
    [InlineData(true, "unsupported", 0, false, false)]
    [InlineData(false, "unsupported", 0, false, false)]
    [InlineData(false, "missing-source", 0, false, false)]
    [InlineData(true, "wrong-id", 0, false, false)]
    [InlineData(false, "wrong-merge-scope", 0, false, false)]
    [InlineData(false, "missing-read-scope", 0, false, false)]
    [InlineData(true, "missing-owner-merge", 0, false, false)]
    [InlineData(false, "response-lost", 1, false, true)]
    [InlineData(true, "response-lost", 1, false, true)]
    [InlineData(false, "bare-403", 1, false, true)]
    [InlineData(true, "bare-403", 1, false, true)]
    [InlineData(false, "cancelled", 1, false, true)]
    [InlineData(true, "cancelled", 1, false, true)]
    [InlineData(false, "malformed-success", 1, false, true)]
    [InlineData(true, "malformed-success", 1, false, true)]
    [InlineData(false, "policy", 1, false, false)]
    [InlineData(true, "policy", 1, false, false)]
    [InlineData(false, "pending", 1, false, false)]
    [InlineData(true, "pending", 1, false, false)]
    [InlineData(false, "wrong-final-source", 1, false, true)]
    [InlineData(true, "wrong-final-route", 1, false, true)]
    [InlineData(false, "untyped-failure", 1, false, true)]
    [InlineData(true, "read-unknown", 0, false, false)]
    [InlineData(false, "read-unknown", 0, false, false)]
    [InlineData(true, "owner-rejected", 1, false, false)]
    [InlineData(false, "network-error", 1, false, true)]
    [InlineData(true, "network-error", 1, false, true)]
    [InlineData(false, "conflicting-channels", 1, false, true)]
    [InlineData(true, "conflicting-channels", 1, false, true)]
    public async Task PreflightAuthorityAndOneDispatchArePreserved(bool owner, string scenario, int merges, bool ok, bool unknown)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var wire = new Wire(fixture, owner, scenario);
        var scopes = new Dictionary<string, string[]>
        {
            ["github_pr_get"] = scenario == "missing-read-scope" ? ["repository.push"] : ["repository.read"],
            ["github_pr_merge"] = scenario == "wrong-merge-scope" ? ["provider.githubie.pull_request.write"] : ["provider.githubie.pull_request.merge"]
        };
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, scopes);
        var provider = new McpRepositoryProvider(Options(owner), new Factory(wire), owner ? null : fixture.Issuer, owner ? null : capability);
        var request = new RepositoryProviderRequest("Display", "fixture", "https://github.com/example/repo.git", null,
            RepositoryOperation.PullRequestMerge, null, null, ProjectId: fixture.Context.Project, OperationId: "merge-case",
            UseAssertion: !owner, ProviderRepositoryId: "registered", UseWindowsOwnerAuthentication: owner,
            PullRequestMerge: new(42, "repository_default", Source.ToUpperInvariant(), " 日本語 message "));

        RepositoryProviderResult raw = await provider.ExecuteAsync(request);
        PullRequestStateResult result = PullRequestStateResult.Parse("Display", "githubie", 42, raw);

        Assert.Equal(merges, wire.MergeCalls);
        Assert.Equal(ok, result.Ok);
        Assert.Equal(unknown, raw.ResultUnknown);
        if (unknown) Assert.Equal("unknown", result.Error!.Outcome);
        if (scenario == "pending") Assert.Equal("pending", result.Error!.Outcome);
        if (scenario is "policy" or "pending") Assert.Equal("merge-correlation", result.Error!.CorrelationId);
        if (scenario == "read-unknown") Assert.Equal("not_executed", result.Error!.Outcome);
        if (!ok) Assert.False(result.Error!.Retryable);
        if (scenario is "wrong-merge-scope" or "missing-read-scope") Assert.Equal(0, wire.Requests);
        if (scenario == "missing-owner-merge") Assert.Equal(0, wire.ReadCalls);
        if (ok) Assert.Equal(Merged, result.PullRequest!.MergeCommit);
        if (wire.MergeArguments is not { } arguments) return;
        Assert.Equal("registered", arguments.GetProperty("repository").GetString());
        Assert.Equal(42, arguments.GetProperty("pull_request_number").GetInt64());
        Assert.Equal(JsonValueKind.Null, arguments.GetProperty("merge_strategy").ValueKind);
        Assert.Equal(Source.ToUpperInvariant(), arguments.GetProperty("expected_source_commit_sha").GetString());
        Assert.Equal(" 日本語 message ", arguments.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(2147483648, "merge", "pull_request_invalid")]
    [InlineData(42, "fast_forward", "merge_strategy_invalid")]
    public async Task ProviderSpecificBoundsRefuseBeforeAnyHttp(long id, string strategy, string code)
    {
        using var wire = new Wire(null, true, "success");
        var provider = new McpRepositoryProvider(Options(true), new Factory(wire));
        var request = new RepositoryProviderRequest("Display", "fixture", "https://github.com/example/repo.git", null,
            RepositoryOperation.PullRequestMerge, null, null, UseWindowsOwnerAuthentication: true,
            PullRequestMerge: new(id, strategy, Source));
        var result = await provider.ExecuteAsync(request);
        Assert.Equal(code, result.ErrorCode);
        Assert.False(result.ResultUnknown);
        Assert.Equal(0, wire.Requests);
    }

    private static McpRepositoryProviderOptions Options(bool owner) => owner ? new("githubie", null, "github")
    {
        AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1",
        RuntimeExecutable = Path.GetFullPath("Githubie.Server.exe"), RuntimeSha256 = new string('A', 64),
        ConnectionResolver = (_, _) => new UserHostConnection(new Uri("http://127.0.0.1:54321/mcp"), Credential, Generation)
    } : new("githubie", new Uri("http://127.0.0.1:54321/mcp"), "github");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandaloneReadDoesNotRequireMergeCapability(bool owner)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var wire = new Wire(fixture, owner, "unsupported");
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true,
            new Dictionary<string, string[]> { ["github_pr_get"] = ["repository.read"] });
        var provider = new McpRepositoryProvider(Options(owner), new Factory(wire), owner ? null : fixture.Issuer, owner ? null : capability);
        var request = new RepositoryProviderRequest("Display", "fixture", "https://github.com/example/repo.git", null,
            RepositoryOperation.PullRequestGet, null, null, ProjectId: fixture.Context.Project, OperationId: "read-case",
            UseAssertion: !owner, UseWindowsOwnerAuthentication: owner, PullRequestRead: new(42));
        var raw = await provider.ExecuteAsync(request);
        var result = PullRequestStateResult.Parse("Display", "githubie", 42, raw);
        Assert.True(result.Ok, result.Error?.ProviderDetail);
        Assert.Equal(Source, result.PullRequest!.SourceCommit);
        Assert.Equal(1, wire.ReadCalls);
        Assert.Equal(0, wire.MergeCalls);
    }

    [Fact]
    public async Task BuckettieMergeRemainsDisabledWithoutAnyRequest()
    {
        using var wire = new Wire(null, false, "success");
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(wire));
        var request = new RepositoryProviderRequest("Display", "fixture", "https://bitbucket.org/example/repo.git", null,
            RepositoryOperation.PullRequestMerge, null, null, UseAssertion: true, PullRequestMerge: new(42, "merge", Source));
        var result = await provider.ExecuteAsync(request);
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.False(result.ResultUnknown);
        Assert.Equal(0, wire.Requests);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class Wire(AssertionFixture? fixture, bool owner, string scenario) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int ReadCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public JsonElement? MergeArguments { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (owner)
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal(Credential, Assert.Single(request.Headers.GetValues("X-Githubie-Session")));
                Assert.Equal(Generation, Assert.Single(request.Headers.GetValues("X-Githubie-Generation")));
            }
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string method = root.GetProperty("method").GetString()!;
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "merge-fixture", version = "2.1.1.0" } };
            else if (method == "tools/list") result = new { tools = ToolNames().Select(name => new { name, inputSchema = new { type = "object" } }).ToArray() };
            else
            {
                Assert.Equal("tools/call", method);
                JsonElement parameters = root.GetProperty("params");
                string tool = parameters.GetProperty("name").GetString()!;
                object payload;
                if (tool == "get_version") payload = new { ok = true, data = "2.1.1.0" };
                else if (tool == "github_provider_capabilities") payload = new { ok = true, data = new
                {
                    pr_get = true, pr_merge = scenario != "unsupported",
                    authentication = new { integration_mode = "windows_owner", direct_connection = "unrestricted",
                        owner_authentication = new { version = 1, mode = "windows_owner", transport = "user_host_schema1", principal = "windows_owner",
                            assertion_required = false, project_binding_required = false, legacy_token_required = false,
                            credential_header = "X-Githubie-Session", generation_header = "X-Githubie-Generation", operations = ToolNames().Where(n => n.StartsWith("github_pr_", StringComparison.Ordinal)).ToArray() } }
                } };
                else
                {
                    Assert.True(tool is "github_pr_get" or "github_pr_merge");
                    if (!owner) await ValidateAssertionAsync(request, tool, cancellationToken);
                    bool merge = tool == "github_pr_merge";
                    if (merge)
                    {
                        MergeCalls++;
                        MergeArguments = parameters.GetProperty("arguments").Clone();
                        if (scenario == "response-lost") throw new HttpRequestException("withheld after dispatch");
                        if (scenario == "cancelled") throw new OperationCanceledException("cancelled after dispatch");
                        if (scenario == "bare-403") return new(HttpStatusCode.Forbidden);
                        if (scenario == "owner-rejected")
                        {
                            var rejected = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("") };
                            rejected.Headers.Add("X-Githubie-Error", "user_host_owner_rejected");
                            return rejected;
                        }
                    }
                    else ReadCalls++;
                    string state = merge || scenario == "already-merged" ? "merged" : scenario == "closed" ? "closed" : "open";
                    payload = merge && scenario == "network-error" ? new { ok = false, error = new { code = "network_error", outcome = "failed" } }
                        : !merge && scenario == "read-unknown" ? new { ok = false, error = new { code = "timeout", outcome = "unknown" } }
                        : scenario == "malformed-success" && merge ? new { ok = true, data = new { } }
                        : merge && scenario is "policy" or "pending" or "untyped-failure"
                        ? new { ok = false, error = new { code = "pull_request_blocked", outcome = scenario == "pending" ? "pending" : scenario == "policy" ? "not_executed" : null, correlation_id = "merge-correlation", retryable = true } }
                        : new { ok = true, data = new { number = scenario == "wrong-id" ? 43 : 42, state,
                            source = "feature", destination = merge && scenario == "wrong-final-route" ? "other" : "main",
                            source_commit_sha = scenario == "missing-source" ? null : scenario == "source-mismatch" || merge && scenario == "wrong-final-source" ? new string('b', 40) : Source,
                            merge_commit_sha = state == "merged" ? Merged : null, draft = scenario == "draft-missing" ? (bool?)null : scenario == "draft",
                            url = "https://github.com/example/repo/pull/42" } };
                }
                object[] content = scenario == "conflicting-channels" && tool == "github_pr_merge"
                    ? [new { type = "text", text = "{\"ok\":false,\"error\":{\"code\":\"pull_request_blocked\",\"outcome\":\"not_executed\"}}" }] : [];
                result = new { isError = false, content, structuredContent = payload };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }

        private string[] ToolNames() => scenario == "missing-owner-merge"
            ? ["get_version", "github_provider_capabilities", "github_pr_get"]
            : ["get_version", "github_provider_capabilities", "github_pr_get", "github_pr_merge"];

        private async Task ValidateAssertionAsync(HttpRequestMessage request, string tool, CancellationToken cancellationToken)
        {
            string token = Assert.IsType<string>(request.Headers.Authorization?.Parameter);
            string encoded = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            var context = fixture!.Context with { Tool = tool, OperationId = claims.RootElement.GetProperty("operation_id").GetString()!,
                Scopes = [tool == "github_pr_get" ? "repository.read" : "provider.githubie.pull_request.merge"] };
            await fixture.Validator(context).ValidateAsync(token, context, cancellationToken);
        }
    }
}
