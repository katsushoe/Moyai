using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class PublicationTransportTests
{
    private static readonly string[] CreateScopes = ["repository.push", "provider.githubie.pull_request.write"];
    private static readonly string[] ReadScopes = ["repository.read"];
    private const string OperationId = "55555555-5555-4555-8555-555555555555";
    private static readonly string Sha = new('a', 40);
    private static readonly string Credential = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData(false, "success", 1, true, false)]
    [InlineData(true, "success", 1, true, false)]
    [InlineData(false, "partial", 1, false, true)]
    [InlineData(true, "partial", 1, false, true)]
    [InlineData(false, "lost", 1, false, true)]
    [InlineData(true, "lost", 1, false, true)]
    [InlineData(false, "bad-sha", 1, false, true)]
    [InlineData(true, "bad-sha", 1, false, true)]
    [InlineData(false, "wrong-id", 1, false, true)]
    [InlineData(true, "wrong-id", 1, false, true)]
    [InlineData(false, "no-create", 0, false, false)]
    [InlineData(true, "no-create", 0, false, false)]
    [InlineData(false, "no-status", 0, false, false)]
    [InlineData(true, "no-status", 0, false, false)]
    [InlineData(false, "string-status", 0, false, false)]
    [InlineData(true, "string-status", 0, false, false)]
    [InlineData(false, "missing-push", 0, false, false)]
    [InlineData(false, "missing-pr", 0, false, false)]
    [InlineData(true, "missing-owner-status", 0, false, false)]
    [InlineData(false, "denied", 1, false, false)]
    [InlineData(true, "denied", 1, false, false)]
    [InlineData(false, "channels", 1, false, true)]
    [InlineData(true, "channels", 1, false, true)]
    [InlineData(false, "bare-403", 1, false, true)]
    [InlineData(true, "bare-403", 1, false, true)]
    public async Task CreateContractBoundariesPreservePartialResultsAndNeverReplay(bool owner, string scenario, int calls, bool ok, bool unknown)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var wire = new Wire(scenario);
        string[] createScopes = scenario == "missing-push" ? ["provider.githubie.pull_request.write"]
            : scenario == "missing-pr" ? ["repository.push"] : ["repository.push", "provider.githubie.pull_request.write"];
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true,
            new Dictionary<string, string[]> { ["github_pr_publish_create"] = createScopes, ["github_pr_publish_status"] = ["repository.read"] });
        var options = Options(owner);
        var provider = new McpRepositoryProvider(options, new Factory(wire), owner ? null : fixture.Issuer, owner ? null : capability);
        var input = new PullRequestPublicationInput("Feature/Exact", "main", Sha.ToUpperInvariant(), " 日本語 title ", OperationId, false);
        var request = new RepositoryProviderRequest("Display", "fixture", "https://github.com/example/repo.git", null,
            RepositoryOperation.PullRequestPublishCreate, null, null, ProjectId: fixture.Context.Project,
            OperationId: "assertion-correlation", UseAssertion: !owner, ProviderRepositoryId: "registered",
            UseWindowsOwnerAuthentication: owner, Publication: input);
        RepositoryProviderResult raw = await provider.ExecuteAsync(request);
        var context = new PublicationRequestContext(OperationId, input.SourceBranch, input.DestinationBranch, Sha);
        var parsed = PullRequestPublicationResult.Parse("Display", "githubie", request.Operation, context, raw);
        Assert.Equal(calls, wire.Creates);
        Assert.Equal(ok, parsed.Ok);
        Assert.Equal(unknown, parsed.RequiresReconciliation);
        Assert.Equal(OperationId, parsed.RequestContext.OperationId);
        if (scenario == "partial")
        {
            Assert.True(parsed.ProviderDataAvailable);
            Assert.Equal("created", parsed.Data!.Value.GetProperty("publication_state").GetString());
            Assert.Equal("unknown", parsed.Data.Value.GetProperty("pull_request_state").GetString());
            Assert.Equal("corr", parsed.Error!.CorrelationId);
        }
        if (!ok) Assert.False(parsed.Error!.Retryable);
        if (!owner && calls > 0)
            Assert.Equal(CreateScopes, wire.LastScopes);
        if (wire.Arguments is { } arguments)
        {
            Assert.Equal("registered", arguments.GetProperty("repository").GetString());
            Assert.Equal("Feature/Exact", arguments.GetProperty("source_branch").GetString());
            Assert.Equal(Sha, arguments.GetProperty("expected_source_sha").GetString());
            Assert.Equal(OperationId, arguments.GetProperty("operation_id").GetString());
            Assert.False(arguments.GetProperty("draft").GetBoolean());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusOnlyStatusCapabilityReadsWithoutCreating(bool owner)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var wire = new Wire("no-create");
        var cap = new AssertionCapability("githubie", "githubie", "1", "ES256", true,
            new Dictionary<string, string[]> { ["github_pr_publish_status"] = ["repository.read"] });
        var provider = new McpRepositoryProvider(Options(owner), new Factory(wire), owner ? null : fixture.Issuer, owner ? null : cap);
        var request = new RepositoryProviderRequest("Display", "fixture", "https://github.com/example/repo.git", null,
            RepositoryOperation.PullRequestPublishStatus, null, null, ProjectId: fixture.Context.Project, OperationId: "status-read",
            UseAssertion: !owner, UseWindowsOwnerAuthentication: owner, PublicationOperationId: OperationId);
        var raw = await provider.ExecuteAsync(request);
        var parsed = PullRequestPublicationResult.Parse("Display", "githubie", request.Operation, new(OperationId, null, null, null), raw);
        Assert.True(parsed.Ok, parsed.Error?.Code);
        Assert.Equal(0, wire.Creates);
        Assert.Equal(1, wire.Statuses);
        Assert.Null(parsed.RequestContext.SourceBranch);
        Assert.Equal(2, wire.Arguments!.Value.EnumerateObject().Count());
        if (!owner) Assert.Equal(ReadScopes, wire.LastScopes);
    }

    [Theory]
    [InlineData("bitbucket")]
    [InlineData("other")]
    public async Task UnsupportedProviderFailsBeforeTransport(string prefix)
    {
        using var wire = new Wire("success");
        var provider = new McpRepositoryProvider(new("test", new Uri("http://127.0.0.1:54321/mcp"), prefix), new Factory(wire));
        var raw = await provider.ExecuteAsync(new("Display", "fixture", "https://example.com/repo", null,
            RepositoryOperation.PullRequestPublishCreate, null, null, UseAssertion: true,
            Publication: new("feature", "main", Sha, "title", OperationId, false)));
        Assert.Equal("provider_capability_missing", raw.ErrorCode);
        Assert.Equal(0, wire.Requests);
    }

    private static McpRepositoryProviderOptions Options(bool owner) => owner ? new("githubie", null, "github")
    {
        AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1",
        RuntimeExecutable = Path.GetFullPath("Githubie.Server.exe"), RuntimeSha256 = new string('A', 64),
        ConnectionResolver = (_, _) => new UserHostConnection(new Uri("http://127.0.0.1:54321/mcp"), Credential, "00000000000000000000000000000001")
    } : new("githubie", new Uri("http://127.0.0.1:54321/mcp"), "github");
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }

    private sealed class Wire(string scenario) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int Creates { get; private set; }
        public int Statuses { get; private set; }
        public JsonElement? Arguments { get; private set; }
        public string[]? LastScopes { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.MethodNotAllowed);
            if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.OK);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string method = root.GetProperty("method").GetString()!;
            object result;
            string[] tools = scenario == "missing-owner-status"
                ? ["get_version", "github_provider_capabilities", "github_pr_publish_create"]
                : ["get_version", "github_provider_capabilities", "github_pr_publish_create", "github_pr_publish_status"];
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(),
                capabilities = new { tools = new { } }, serverInfo = new { name = "publication-fixture", version = "2.1.4.0" } };
            else if (method == "tools/list") result = new { tools = tools.Select(name => new { name, inputSchema = new { type = "object" } }) };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                object payload;
                if (tool == "get_version") payload = new { ok = true, data = "2.1.4.0" };
                else if (tool == "github_provider_capabilities") payload = new { ok = true, data = new {
                    pull_request_publish_create = scenario != "no-create",
                    pull_request_publish_status = scenario == "string-status" ? (object)"true" : scenario != "no-status",
                    authentication = new { integration_mode = "windows_owner", direct_connection = "unrestricted", owner_authentication = new {
                        version = 1, mode = "windows_owner", transport = "user_host_schema1", principal = "windows_owner",
                        assertion_required = false, project_binding_required = false, legacy_token_required = false,
                        credential_header = "X-Githubie-Session", generation_header = "X-Githubie-Generation", operations = tools } } } };
                else
                {
                    bool create = tool == "github_pr_publish_create";
                    Assert.True(create || tool == "github_pr_publish_status");
                    if (create) Creates++; else Statuses++;
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    if (request.Headers.Authorization?.Parameter is { } token)
                    {
                        string encoded = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
                        using JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
                        LastScopes = claims.RootElement.GetProperty("scope").EnumerateArray().Select(x => x.GetString()!).ToArray();
                    }
                    if (scenario == "lost") throw new HttpRequestException("response withheld");
                    if (scenario == "bare-403") return new(HttpStatusCode.Forbidden);
                    bool denied = scenario == "denied", partial = scenario == "partial";
                    payload = new { ok = !denied && !partial, operation = create ? "pull_request_publish_create" : "pull_request_publish_status",
                        data = new { operation_id = scenario == "wrong-id" ? "66666666-6666-4666-8666-666666666666" : OperationId,
                            source_branch = "Feature/Exact", destination_branch = "main", source_sha = scenario == "bad-sha" ? new string('b',40) : Sha,
                            publication_state = denied ? "not_attempted" : "created", pull_request_state = denied ? "not_attempted" : partial ? "unknown" : "already_present",
                            pull_request_number = denied || partial ? (int?)null : 42, pull_request_url = denied || partial ? null : "https://github.com/example/repo/pull/42",
                            pull_request_head_sha = denied || partial ? null : Sha, observed_remote_sha = denied ? null : Sha,
                            observed_at = "2026-11-02T00:00:00Z", stage = denied ? "approval" : "pull_request", requires_reconciliation = partial },
                        error = denied || partial ? new { code = denied ? "approval_denied" : "pull_request_result_unknown", outcome = denied ? "not_executed" : "unknown", correlation_id = "corr" } : null };
                }
                object[] content = scenario == "channels" && tool == "github_pr_publish_create"
                    ? [new { type = "text", text = "{}" }] : [];
                result = new { isError = false, content, structuredContent = payload };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}

