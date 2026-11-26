using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;
using Moyai.Infrastructure.Tests.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class BranchProtectionTransportTests
{
    private const string Generation = "00000000000000000000000000000001";
    private static readonly string Credential = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData(false, RepositoryOperation.BranchProtectionGet)]
    [InlineData(true, RepositoryOperation.BranchProtectionGet)]
    [InlineData(false, RepositoryOperation.BranchProtectionUpdate)]
    [InlineData(true, RepositoryOperation.BranchProtectionUpdate)]
    [InlineData(false, RepositoryOperation.BranchProtectionDelete)]
    [InlineData(true, RepositoryOperation.BranchProtectionDelete)]
    public async Task AuthenticatedOperationsMatchWire(bool owner, RepositoryOperation operation)
    {
        var (result, calls, arguments, _) = await RunAsync(owner, operation, "success");
        Assert.True(result.Ok, result.Error?.Code);
        Assert.Equal(1, calls);
        Assert.Equal("registered", arguments!.Value.GetProperty("repository").GetString());
        Assert.Equal("main", arguments.Value.GetProperty("branch").GetString());
        if (operation == RepositoryOperation.BranchProtectionUpdate)
        {
            Assert.False(arguments.Value.GetProperty("create_if_missing").GetBoolean());
            Assert.False(arguments.Value.GetProperty("edit").GetProperty("allow_force_pushes").GetBoolean());
        }
    }

    [Theory]
    [InlineData(false, "missing-capability", 0, false)]
    [InlineData(true, "missing-capability", 0, false)]
    [InlineData(true, "missing-tool", 0, false)]
    [InlineData(false, "wrong-scope", 0, false)]
    [InlineData(false, "response-lost", 1, true)]
    [InlineData(true, "response-lost", 1, true)]
    [InlineData(false, "cancelled", 1, true)]
    [InlineData(true, "cancelled", 1, true)]
    [InlineData(false, "bare403", 1, true)]
    [InlineData(true, "bare403", 1, true)]
    [InlineData(false, "network-error", 1, true)]
    [InlineData(true, "network-error", 1, true)]
    [InlineData(false, "conflicting-channels", 1, true)]
    [InlineData(true, "conflicting-channels", 1, true)]
    [InlineData(false, "conflicting-success", 1, true)]
    [InlineData(true, "conflicting-success", 1, true)]
    [InlineData(false, "wrong-branch", 1, true)]
    [InlineData(true, "wrong-branch", 1, true)]
    [InlineData(false, "access-denied", 1, false)]
    [InlineData(true, "access-denied", 1, false)]
    public async Task RefusalUnknownAndNonReplayBoundaries(bool owner, string scenario, int expectedCalls, bool unknown)
    {
        var (result, calls, _, requests) = await RunAsync(owner, RepositoryOperation.BranchProtectionUpdate, scenario);
        Assert.False(result.Ok);
        Assert.Equal(expectedCalls, calls);
        Assert.Equal(unknown, result.Error!.Outcome == "unknown");
        Assert.False(result.Error.Retryable);
        Assert.DoesNotContain("private-do-not-return", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        if (scenario == "wrong-scope") Assert.Equal(0, requests);
        if (scenario == "access-denied")
        {
            Assert.Equal("branch_protection_access_denied", result.Error.Code);
            Assert.Equal("verify_permissions", result.Error.SuggestedAction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoopIsSuccessfulWithoutInventedApprovalField(bool owner)
    {
        var (result, calls, _, _) = await RunAsync(owner, RepositoryOperation.BranchProtectionUpdate, "noop");
        Assert.True(result.Ok);
        Assert.False(result.Protection!.Changed);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(RepositoryOperation.BranchProtectionGet, "github")]
    [InlineData(RepositoryOperation.BranchProtectionUpdate, "github")]
    [InlineData(RepositoryOperation.BranchProtectionDelete, "github")]
    [InlineData(RepositoryOperation.BranchProtectionGet, "bitbucket")]
    [InlineData(RepositoryOperation.BranchProtectionUpdate, "bitbucket")]
    [InlineData(RepositoryOperation.BranchProtectionDelete, "bitbucket")]
    public async Task LegacyOrUnsupportedProviderIsRefusedBeforeHttp(RepositoryOperation operation, string prefix)
    {
        await using var fixture = new AssertionFixture();
        using var wire = new Wire(fixture, false, "success");
        var provider = new McpRepositoryProvider(new(prefix == "github" ? "githubie" : "buckettie", new Uri("http://127.0.0.1:54321/mcp"), prefix), new Factory(wire));
        var result = await provider.ExecuteAsync(new("display", "source", "https://github.com/example/repo", null, operation, null, null, Branch: "main", ProtectionEdit: new(EnforceAdmins: true)));
        Assert.False(result.Ok);
        Assert.Equal(prefix == "github" ? "auth_scope_denied" : "provider_capability_missing", result.ErrorCode);
        Assert.Equal(0, wire.Requests);
    }

    [Fact]
    public async Task UnsupportedProviderAlwaysAdvertisesHostingProtectionAsFalse()
    {
        await using var fixture = new AssertionFixture();
        using var wire = new Wire(fixture, false, "success");
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://127.0.0.1:54321/mcp"), "bitbucket"), new Factory(wire));
        var result = await provider.ExecuteAsync(new("display", "source", "https://bitbucket.org/example/repo", null, RepositoryOperation.ProviderCapabilities, null, null));
        Assert.True(result.Ok);
        using JsonDocument json = JsonDocument.Parse(result.Output!);
        Assert.False(json.RootElement.GetProperty("data").GetProperty("branch_protection_read").GetBoolean());
        Assert.False(json.RootElement.GetProperty("data").GetProperty("branch_protection_write").GetBoolean());
        Assert.Equal(0, wire.Calls);
    }

    private static async Task<(BranchProtectionResult Result, int Calls, JsonElement? Arguments, int Requests)> RunAsync(bool owner, RepositoryOperation operation, string scenario)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var wire = new Wire(fixture, owner, scenario);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]>
        {
            ["github_branch_protection_get"] = ["repository.read"],
            ["github_branch_protection_update"] = [scenario == "wrong-scope" ? "repository.write" : "provider.githubie.branch_protection.write"],
            ["github_branch_protection_delete"] = ["provider.githubie.branch_protection.write"]
        });
        var options = owner ? new McpRepositoryProviderOptions("githubie", null, "github")
        {
            AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1", RuntimeExecutable = Path.GetFullPath("Githubie.Server.exe"),
            RuntimeSha256 = new string('A', 64), ConnectionResolver = (_, _) => new UserHostConnection(new Uri("http://127.0.0.1:54321/mcp"), Credential, Generation)
        } : new McpRepositoryProviderOptions("githubie", new Uri("http://127.0.0.1:54321/mcp"), "github");
        var provider = new McpRepositoryProvider(options, new Factory(wire), owner ? null : fixture.Issuer, owner ? null : capability);
        var request = new RepositoryProviderRequest("display", "source", "https://github.com/example/repo.git", null, operation, null, null,
            Branch: "main", ProjectId: fixture.Context.Project, OperationId: "protection-case", UseAssertion: !owner, ProviderRepositoryId: "registered",
            UseWindowsOwnerAuthentication: owner, ProtectionEdit: operation == RepositoryOperation.BranchProtectionUpdate ? new(AllowForcePushes: false) : null);
        var raw = await provider.ExecuteAsync(request);
        return (BranchProtectionResult.Parse("display", "githubie", "main", operation, raw), wire.Calls, wire.Arguments, wire.Requests);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }

    private sealed class Wire(AssertionFixture fixture, bool owner, string scenario) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int Requests { get; private set; }
        public JsonElement? Arguments { get; private set; }
        private static string[] Tools => ["get_version", "github_provider_capabilities", "github_branch_protection_get", "github_branch_protection_delete", "github_branch_protection_update"];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (owner)
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal(Credential, request.Headers.GetValues("X-Githubie-Session").Single());
                Assert.Equal(Generation, request.Headers.GetValues("X-Githubie-Generation").Single());
            }
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out var id)) return new(HttpStatusCode.Accepted);
            string method = root.GetProperty("method").GetString()!;
            object response;
            if (method == "initialize") response = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "protection-fixture", version = "2.1.2.0" } };
            else if (method == "server/discover") response = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "tools/list") response = new { tools = Tools.Where(name => scenario != "missing-tool" || name != "github_branch_protection_update").Select(name => new { name, inputSchema = new { type = "object" } }).ToArray() };
            else if (method == "ping") response = new { };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                object payload;
                if (tool == "get_version") payload = new { ok = true, data = "2.1.2.0" };
                else if (tool is "github_provider_capabilities" or "bitbucket_provider_capabilities") payload = new { ok = true, data = new
                {
                    branch_protection_read = true, branch_protection_write = scenario != "missing-capability",
                    authentication = new { integration_mode = "windows_owner", direct_connection = "unrestricted", owner_authentication = new
                    {
                        version = 1, mode = "windows_owner", transport = "user_host_schema1", principal = "windows_owner", assertion_required = false,
                        project_binding_required = false, legacy_token_required = false, credential_header = "X-Githubie-Session", generation_header = "X-Githubie-Generation",
                        operations = Tools.Where(name => name.StartsWith("github_branch_", StringComparison.Ordinal)).ToArray()
                    } }
                } };
                else
                {
                    Calls++;
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    if (!owner)
                    {
                        string token = request.Headers.Authorization!.Parameter!;
                        string encoded = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
                        using var claims = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
                        var context = fixture.Context with { Tool = tool, OperationId = claims.RootElement.GetProperty("operation_id").GetString()!, Scopes = [tool.EndsWith("_get", StringComparison.Ordinal) ? "repository.read" : "provider.githubie.branch_protection.write"] };
                        await fixture.Validator(context).ValidateAsync(token, context, cancellationToken);
                    }
                    if (scenario == "response-lost") throw new HttpRequestException("private-do-not-return");
                    if (scenario == "cancelled") throw new OperationCanceledException("private-do-not-return");
                    if (scenario == "bare403") return new(HttpStatusCode.Forbidden);
                    bool get = tool.EndsWith("_get", StringComparison.Ordinal);
                    bool delete = tool.EndsWith("_delete", StringComparison.Ordinal);
                    payload = scenario == "network-error" ? new { ok = false, error = new { code = "network_error", outcome = "failed" } }
                        : scenario == "access-denied" ? new { ok = false, error = new { code = "branch_protection_access_denied", outcome = "not_executed", suggested_action = "verify_permissions", message = "private-do-not-return" } }
                        : (object)new { ok = true, data = new { branch = scenario == "wrong-branch" ? "other" : "main", can_update = get,
                            changed = scenario != "noop", policy = delete ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(BranchProtectionContractTests.Policy) } };
                }
                object[] content = scenario == "conflicting-success" && tool == "github_branch_protection_update"
                    ? [new { type = "text", text = "{\"ok\":true,\"data\":{\"branch\":\"main\",\"changed\":false,\"policy\":null}}" }]
                    : scenario == "conflicting-channels" && tool == "github_branch_protection_update"
                    ? [new { type = "text", text = "{\"ok\":false,\"error\":{\"code\":\"branch_protection_access_denied\",\"outcome\":\"not_executed\"}}" }] : [];
                response = new { isError = false, content, structuredContent = payload };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = response }), Encoding.UTF8, "application/json") };
        }
    }
}
