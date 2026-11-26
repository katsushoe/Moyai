using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class WindowsOwnerAuthenticationTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:54321/mcp");
    private static string Credential => Convert.ToBase64String(Enumerable.Repeat((byte)251, 32).ToArray());
    private const string Version = "{\"version\":\"2.0.1.0\"}";
    private const string Capabilities = """
        {"contract_version":3,"operations":{"repository_commit":true,"repository_commit_selected":true,"release_publish":true},
         "authentication":{"provider_id":"buckettie","authentication_mode":"windows_owner","owner_protocol_version":1,
         "requires_assertion":false,"project_binding_required":false,"integration_mode":"moyai","direct_connection":"unrestricted",
         "replay_protection":false,"required_audience":null,"protocol_version":null,"algorithm":null,"tool_scopes":{},
         "supported_tools":["bitbucket_repository_status","bitbucket_repository_commit","bitbucket_repository_commit_selected","bitbucket_fetch","bitbucket_pull","buckettie_release_publish"]}}
        """;

    [Fact]
    public void ExplicitContractAdmitsOnlySupportedOperations()
    {
        WindowsOwnerContract contract = WindowsOwnerContract.Parse(Version, Capabilities);
        contract.Require("bitbucket_repository_status");
        contract.Require("bitbucket_repository_commit");
        Assert.Throws<ProviderAuthenticationException>(() => contract.Require("bitbucket_push"));
        Assert.False(WindowsOwnerContract.IsRead("bitbucket_fetch"));
        Assert.False(WindowsOwnerContract.IsRead("bitbucket_history_rewrite_execute"));
        Assert.True(WindowsOwnerContract.IsRead("bitbucket_history_rewrite_preview"));
    }

    [Theory]
    [InlineData("authentication_mode", "es256")]
    [InlineData("provider_id", "githubie")]
    [InlineData("integration_mode", "standalone")]
    [InlineData("direct_connection", "restricted")]
    [InlineData("owner_protocol_version", 2)]
    [InlineData("requires_assertion", true)]
    [InlineData("project_binding_required", true)]
    [InlineData("replay_protection", true)]
    [InlineData("algorithm", "ES256")]
    public void IncompatibleContractNeverInfersSupport(string field, object value)
    {
        JsonNode root = JsonNode.Parse(Capabilities)!;
        root["authentication"]![field] = JsonSerializer.SerializeToNode(value);
        Assert.Equal("provider_capability_missing", Assert.Throws<ProviderAuthenticationException>(() =>
            WindowsOwnerContract.Parse(Version, root.ToJsonString())).Code);
    }

    [Theory]
    [InlineData("2.0.0.9")]
    [InlineData("2.0.1")]
    [InlineData("garbage")]
    public void UnsupportedApplicationVersionIsRejected(string version) => Assert.Throws<ProviderAuthenticationException>(() =>
        WindowsOwnerContract.Parse(JsonSerializer.Serialize(new { version }), Capabilities));

    [Fact]
    public void MissingFlagsAndDuplicatePropertiesAreRejected()
    {
        JsonNode root = JsonNode.Parse(Capabilities)!;
        root["operations"]!["repository_commit"] = false;
        WindowsOwnerContract contract = WindowsOwnerContract.Parse(Version, root.ToJsonString());
        Assert.Throws<ProviderAuthenticationException>(() => contract.Require("bitbucket_repository_commit"));
        Assert.Throws<ProviderAuthenticationException>(() => WindowsOwnerContract.Parse(Version,
            Capabilities.Replace("\"contract_version\":3", "\"contract_version\":3,\"contract_version\":3", StringComparison.Ordinal)));
        root["authentication"]!.AsObject().Remove("requires_assertion");
        Assert.Throws<ProviderAuthenticationException>(() => WindowsOwnerContract.Parse(Version, root.ToJsonString()));
    }

    [Theory]
    [InlineData(RepositoryOperation.Status)]
    [InlineData(RepositoryOperation.Commit)]
    [InlineData(RepositoryOperation.Pull)]
    public async Task OwnerCallsRequireNoSignerOrTokenAndCarryNoAuthorization(RepositoryOperation operation)
    {
        using var handler = new WireHandler();
        var provider = new McpRepositoryProvider(Options(), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(operation));
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(new[] { "get_version", "bitbucket_provider_capabilities", RepositoryProviderContract.ToolName("bitbucket", operation) }, handler.Tools);
        Assert.All(handler.Credentials, value => Assert.Equal(Credential, value));
        Assert.DoesNotContain(Credential, result.Output ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(JsonSerializer.Serialize(Credential)[1..^1], result.Output ?? "", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("assertion")]
    [InlineData("token")]
    [InlineData("default-header")]
    public async Task MixedAuthenticationNeverSendsHttp(string kind)
    {
        using var handler = new WireHandler();
        var provider = new McpRepositoryProvider(Options(), new Factory(handler, kind == "default-header"));
        RepositoryProviderRequest request = Request(RepositoryOperation.Status);
        request = kind switch
        {
            "mode" => request with { UseWindowsOwnerAuthentication = false },
            "assertion" => request with { UseAssertion = true },
            "token" => request with { ServiceToken = "unused-fixture-token" },
            _ => request,
        };
        Assert.Equal("provider_authentication_mode_mismatch", (await provider.ExecuteAsync(request)).ErrorCode);
        Assert.Empty(handler.Credentials);
    }

    [Fact]
    public async Task ReadyGenerationMismatchNeverSendsHttp()
    {
        using var handler = new WireHandler();
        var provider = new McpRepositoryProvider(Options() with { ExpectedGeneration = "another-generation" }, new Factory(handler));
        Assert.Equal("host_discovery_stale", (await provider.ExecuteAsync(Request(RepositoryOperation.Commit))).ErrorCode);
        Assert.Empty(handler.Credentials);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("mode")]
    [InlineData("support")]
    public async Task UnsupportedProviderNeverReceivesBusinessTool(string kind)
    {
        using var handler = new WireHandler { InvalidContract = kind };
        RepositoryProviderResult result = await new McpRepositoryProvider(Options(), new Factory(handler)).ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.False(result.ResultUnknown);
        Assert.DoesNotContain("bitbucket_repository_commit", handler.Tools);
    }

    [Theory]
    [InlineData("disconnect", true)]
    [InlineData("owner-refusal", false)]
    [InlineData("busy", false)]
    [InlineData("unknown", true)]
    [InlineData("invalid-response", true)]
    public async Task MutationIsNeverReplayedAndRetainsUnknown(string failure, bool unknown)
    {
        using var handler = new WireHandler { Failure = failure };
        RepositoryProviderResult result = await new McpRepositoryProvider(Options(), new Factory(handler)).ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.False(result.Ok);
        Assert.Equal(unknown, result.ResultUnknown);
        Assert.Equal(1, handler.Tools.Count(tool => tool == "bitbucket_repository_commit"));
        if (failure == "busy") Assert.Equal("repository_busy", result.ErrorCode);
    }

    [Theory]
    [InlineData(RepositoryOperation.Status, 2, false)]
    [InlineData(RepositoryOperation.Pull, 1, true)]
    public async Task OnlyReadMayReconnect(RepositoryOperation operation, int expected, bool unknown)
    {
        using var handler = new WireHandler { Failure = "disconnect" };
        int resolutions = 0;
        var options = Options() with { ConnectionResolver = (_, _) =>
        { resolutions++; return new UserHostConnection(Endpoint, Credential, "generation"); } };
        RepositoryProviderResult result = await new McpRepositoryProvider(options, new Factory(handler)).ExecuteAsync(Request(operation));
        Assert.Equal(expected, resolutions);
        Assert.Equal(unknown, result.ResultUnknown);
    }

    [Theory]
    [InlineData("", true, false)]
    [InlineData("disconnect", false, true)]
    [InlineData("busy", false, false)]
    public async Task LifecycleUsesOwnerPathAndNeverReplays(string failure, bool ok, bool unknown)
    {
        using var handler = new WireHandler { Failure = failure };
        var provider = new McpLifecycleProvider(Options(), new Factory(handler));
        var request = new LifecycleRequest("OwnerFixture", "fixture", null, LifecycleAction.ReleasePublish, "1.0", null, null, null,
            UseWindowsOwnerAuthentication: true);
        LifecycleResult result = await provider.ExecuteAsync(request);
        Assert.Equal(ok, result.Ok);
        Assert.Equal(unknown, result.ResultUnknown);
        Assert.Single(handler.Tools, tool => tool == "buckettie_release_publish");
    }

    private static McpRepositoryProviderOptions Options() => new("buckettie", null, "bitbucket")
    {
        AuthenticationMode = "windows_owner", ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = Path.GetFullPath("owner-fixture-runtime"),
        ConnectionResolver = (_, _) => new UserHostConnection(Endpoint, Credential, "generation"),
    };
    private static RepositoryProviderRequest Request(RepositoryOperation operation) => new("OwnerFixture", "fixture", "https://bitbucket.org/example/repo.git", null,
        operation, "fixture commit", null, UseWindowsOwnerAuthentication: true);

    private sealed class Factory(HttpMessageHandler handler, bool authorization = false) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, false);
            if (authorization) client.DefaultRequestHeaders.Authorization = new("Bearer", "unused-fixture-token");
            return client;
        }
    }

    private sealed class WireHandler : HttpMessageHandler
    {
        public string? InvalidContract { get; init; }
        public string? Failure { get; init; }
        public List<string> Tools { get; } = [];
        public List<string> Credentials { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Credentials.Add(request.Headers.GetValues("X-Buckettie-Session").Single());
            Assert.Null(request.Headers.Authorization);
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "owner-fixture", version = "2.0.1.0" } };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                Tools.Add(tool);
                string payload;
                if (tool == "get_version") payload = InvalidContract == "version" ? "{\"version\":\"2.0.0.1\"}" : Version;
                else if (tool == "bitbucket_provider_capabilities") payload = InvalidContract switch
                {
                    "mode" => Capabilities.Replace("windows_owner", "es256", StringComparison.Ordinal),
                    "support" => Capabilities.Replace("\"repository_commit\":true", "\"repository_commit\":false", StringComparison.Ordinal),
                    _ => Capabilities,
                };
                else
                {
                    if (Failure == "disconnect") throw new HttpRequestException("fixture connection lost");
                    if (Failure == "owner-refusal") return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"user_host_credential_invalid\",\"outcome\":\"not_executed\"}}");
                    if (Failure == "busy") return Json(HttpStatusCode.ServiceUnavailable, "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"busy\",\"data\":{\"code\":\"repository_busy\",\"outcome\":\"not_executed\"}}}");
                    payload = Failure switch
                    {
                        "unknown" => "{\"ok\":false,\"error\":{\"code\":\"provider_result_unknown\",\"outcome\":\"unknown\"}}",
                        "invalid-response" => "{}",
                        _ => JsonSerializer.Serialize(new { ok = true, data = new { reflection = Credential } }),
                    };
                }
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = JsonSerializer.Deserialize<JsonElement>(payload) };
            }
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string payload) => new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}
