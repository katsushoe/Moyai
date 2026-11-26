using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;
using Moyai.GithubieOwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class GithubieOwnerSessionTests
{
    private const string Generation = "00000000000000000000000000000001";
    private static readonly string Credential = Convert.ToBase64String(new byte[32]);
    private static McpRepositoryProviderOptions Options() => new("githubie", null, "github")
    {
        AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1",
        RuntimeExecutable = Path.GetFullPath("Githubie.Server.exe"), RuntimeSha256 = new string('A', 64),
        ConnectionResolver = (_, _) => new UserHostConnection(new Uri("http://127.0.0.1:54321/mcp"), Credential, Generation),
    };

    private static RepositoryProviderRequest Request(RepositoryOperation operation) => new("fixture-repository", "fixture", "https://example.invalid/repo", null,
        operation, "test", null, UseWindowsOwnerAuthentication: true);

    [Fact]
    public async Task EveryRequestCarriesBothHeadersAndCapabilitiesUseRepository()
    {
        using var handler = new Wire();
        var provider = new McpRepositoryProvider(Options(), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Status));
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Contains("tools/list", handler.Methods);
        Assert.Equal("fixture-repository", handler.CapabilityRepository);
        Assert.Contains("github_repository_status", handler.BusinessTools);
        Assert.True(handler.Requests > 3);
    }

    [Fact]
    public async Task MetadataProbeUsesPingListAndVersionWithoutBusinessDispatch()
    {
        using var handler = new Wire();
        int count = await GithubieMetadataProbe.RunAsync(Options(), new Factory(handler), CancellationToken.None);
        Assert.Equal(3, count);
        Assert.Contains("ping", handler.Methods);
        Assert.Contains("tools/list", handler.Methods);
        Assert.Null(handler.CapabilityRepository);
        Assert.Empty(handler.BusinessTools);
    }

    [Theory]
    [InlineData("wrong-version")]
    [InlineData("missing-version-tool")]
    public async Task MetadataProbeRejectsUnsupportedVersionOrMissingToolWithoutBusinessDispatch(string failure)
    {
        using var handler = new Wire { Failure = failure };
        var error = await Assert.ThrowsAsync<Moyai.Application.Authentication.ProviderAuthenticationException>(() =>
            GithubieMetadataProbe.RunAsync(Options(), new Factory(handler), CancellationToken.None));
        Assert.Equal("provider_capability_missing", error.Code);
        Assert.Null(handler.CapabilityRepository);
        Assert.Empty(handler.BusinessTools);
    }

    [Theory]
    [InlineData("owner-rejected", false)]
    [InlineData("disconnect", true)]
    [InlineData("bare-403", true)]
    [InlineData("owner-rejected-body", true)]
    [InlineData("network-error", true)]
    [InlineData("redirect", true)]
    public async Task MutationIsNeverReplayedAndUnknownOnlyForUnprovenOutcome(string failure, bool unknown)
    {
        using var handler = new Wire { Failure = failure };
        var provider = new McpRepositoryProvider(Options(), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.False(result.Ok);
        Assert.Equal(unknown, result.ResultUnknown);
        Assert.Single(handler.BusinessTools);
    }

    [Fact]
    public async Task OwnerRejectionDoesNotReplayBusinessRead()
    {
        using var handler = new Wire { Failure = "owner-rejected" };
        var provider = new McpRepositoryProvider(Options(), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Status));
        Assert.False(result.Ok);
        Assert.False(result.ResultUnknown);
        Assert.Single(handler.BusinessTools);
    }

    [Fact]
    public async Task WrongGenerationNeverSendsAnyRequest()
    {
        using var handler = new Wire();
        var provider = new McpRepositoryProvider(Options() with { ExpectedGeneration = "00000000000000000000000000000002" }, new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.False(result.Ok);
        Assert.False(result.ResultUnknown);
        Assert.Equal("host_discovery_stale", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("wrong-version")]
    [InlineData("wrong-mode")]
    [InlineData("missing-tool")]
    [InlineData("missing-capability")]
    public async Task MissingOwnerContractOrAdvertisedToolNeverDispatches(string failure)
    {
        using var handler = new Wire { Failure = failure };
        var provider = new McpRepositoryProvider(Options(), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.False(result.Ok);
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.Empty(handler.BusinessTools);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }

    private sealed class Wire : HttpMessageHandler
    {
        public string? Failure { get; init; }
        public int Requests { get; private set; }
        public string? CapabilityRepository { get; private set; }
        public List<string> Methods { get; } = [];
        public List<string> BusinessTools { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(Credential, request.Headers.GetValues("X-Githubie-Session").Single());
            Assert.Equal(Generation, request.Headers.GetValues("X-Githubie-Generation").Single());
            Assert.Null(request.Headers.Authorization);
            using JsonDocument doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = doc.RootElement;
            string method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            object result;
            if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "fixture", version = "2.1.0.0" } };
            else if (method == "ping") result = new { };
            else if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "tools/list") result = new { tools = Failure == "missing-version-tool"
                ? new[] { new { name = "github_repository_status", inputSchema = new { type = "object" } } }
                : Failure == "missing-tool"
                ? new[] { new { name = "get_version", inputSchema = new { type = "object" } }, new { name = "github_repository_status", inputSchema = new { type = "object" } } }
                : new[] { new { name = "get_version", inputSchema = new { type = "object" } }, new { name = "github_repository_status", inputSchema = new { type = "object" } }, new { name = "github_repository_commit", inputSchema = new { type = "object" } } } };
            else
            {
                JsonElement parameters = root.GetProperty("params");
                string tool = parameters.GetProperty("name").GetString()!;
                string payload;
                if (tool == "get_version") payload = Failure == "wrong-version" ? "{\"ok\":true,\"data\":\"2.0.0.0\"}" : "{\"ok\":true,\"data\":\"2.1.0.0\"}";
                else if (tool == "github_provider_capabilities")
                {
                    CapabilityRepository = parameters.GetProperty("arguments").GetProperty("repository").GetString();
                    payload = """{"ok":true,"data":{"repository_commit":true,"authentication":{"integration_mode":"windows_owner","direct_connection":"unrestricted","owner_authentication":{"version":1,"mode":"windows_owner","transport":"user_host_schema1","principal":"windows_owner","assertion_required":false,"project_binding_required":false,"legacy_token_required":false,"credential_header":"X-Githubie-Session","generation_header":"X-Githubie-Generation","operations":["github_repository_status","github_repository_commit"]}}}}""";
                    if (Failure == "wrong-mode") payload = payload.Replace("windows_owner", "legacy", StringComparison.Ordinal);
                    if (Failure == "missing-capability") payload = payload.Replace("\"repository_commit\":true", "\"repository_commit\":false", StringComparison.Ordinal);
                }
                else
                {
                    BusinessTools.Add(tool);
                    if (Failure == "disconnect") throw new HttpRequestException("lost");
                    if (Failure == "redirect") return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://127.0.0.1:54322/mcp") } };
                    if (Failure is "owner-rejected" or "owner-rejected-body" or "bare-403")
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(Failure == "owner-rejected-body" ? "unexpected" : "") };
                        if (Failure != "bare-403") response.Headers.Add("X-Githubie-Error", "user_host_owner_rejected");
                        return response;
                    }
                    payload = Failure == "network-error" ? "{\"ok\":false,\"error\":{\"code\":\"network_error\",\"outcome\":\"failed\"}}" : "{\"ok\":true,\"data\":{}}";
                }
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = JsonSerializer.Deserialize<JsonElement>(payload) };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
