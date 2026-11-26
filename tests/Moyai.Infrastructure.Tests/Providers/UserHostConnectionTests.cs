using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;
using Moyai.Infrastructure.Tests.Authentication;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class UserHostConnectionTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:54321/mcp");
    private static string Credential => Convert.ToBase64String(Enumerable.Repeat((byte)251, 32).ToArray());

    [Theory]
    [InlineData("/")]
    [InlineData("/mcp")]
    [InlineData("/custom/mcp")]
    public void CanonicalPathsAreAccepted(string path) => BuckettieUserHostResolver.ValidateMcpPath(path);

    [Theory]
    [InlineData("")]
    [InlineData("mcp")]
    [InlineData("//mcp")]
    [InlineData("/a/../mcp")]
    [InlineData("/mcp?x=1")]
    [InlineData("/mcp#x")]
    [InlineData("/mcp\\other")]
    [InlineData("/%2e%2e/mcp")]
    public void NoncanonicalPathsAreRejected(string path) => Assert.Throws<ArgumentException>(() => BuckettieUserHostResolver.ValidateMcpPath(path));

    [Theory]
    [InlineData("{\"schema_version\":1,\"schema_version\":1}")]
    [InlineData("{\"future\":{\"x\":1,\"x\":2}}")]
    public void DuplicateNamesAreRejected(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("host_discovery_invalid", Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.ValidateProperties(doc.RootElement)).Code);
    }

    [Fact]
    public void ConnectionDoesNotPrintCredentialAndRedactsJsonEscaping()
    {
        using var connection = new UserHostConnection(Endpoint, Credential, "generation");
        Assert.DoesNotContain(Credential, connection.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Credential, connection.Redact(Credential), StringComparison.Ordinal);
        Assert.DoesNotContain(JsonSerializer.Serialize(Credential)[1..^1], connection.Redact(JsonSerializer.Serialize(Credential)), StringComparison.Ordinal);
        connection.Dispose();
        Assert.Empty(connection.Credential);
    }

    [Theory]
    [InlineData(401, "user_host_credential_required", "not_executed", true)]
    [InlineData(401, "user_host_credential_invalid", "not_executed", true)]
    [InlineData(403, "user_host_loopback_required", "not_executed", true)]
    [InlineData(403, "user_host_origin_rejected", "not_executed", true)]
    [InlineData(401, "auth_project_mismatch", "not_executed", false)]
    [InlineData(403, "user_host_credential_invalid", "not_executed", false)]
    [InlineData(401, "user_host_credential_invalid", "unknown", false)]
    [InlineData(500, "user_host_credential_invalid", "not_executed", false)]
    public void OnlyExactOwnerGateRejectionProvesNoExecution(int status, string code, string outcome, bool expected) =>
        Assert.Equal(expected, ProviderConnection.IsOwnerRejection(JsonSerializer.Serialize(new { error = new { code, outcome } }), (HttpStatusCode)status));

    [Fact]
    public async Task AllHttpGetsOwnerCredentialAndOnlySignedToolGetsAssertion()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var handler = new WireHandler();
        var options = Options((_, _) => new UserHostConnection(Endpoint, Credential, "generation"));
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
            new Dictionary<string, string[]> { ["bitbucket_repository_status"] = ["repository.read"] });
        var provider = new McpRepositoryProvider(options, new Factory(handler), fixture.Issuer, capability);
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Status) with { UseAssertion = true, ProjectId = fixture.Context.Project });
        Assert.True(result.Ok, result.ErrorMessage);
        Assert.NotEmpty(handler.OwnerCredentials);
        Assert.All(handler.OwnerCredentials, value => Assert.Equal(Credential, value));
        Assert.Single(handler.Assertions);
        Assert.False(string.IsNullOrEmpty(handler.Assertions[0]));
        Assert.DoesNotContain(Credential, result.Output ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(JsonSerializer.Serialize(Credential)[1..^1], result.Output ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadReconnectsAtMostOnceAndUsesFreshResolution()
    {
        int resolutions = 0;
        using var handler = new WireHandler { RejectAll = true };
        var provider = new McpRepositoryProvider(Options((_, _) =>
        { resolutions++; return new UserHostConnection(Endpoint, Credential, resolutions.ToString(System.Globalization.CultureInfo.InvariantCulture)); }), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Status));
        Assert.False(result.Ok);
        Assert.Equal(2, resolutions);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MutationIsNeverReplayedAndOnlyOwnerGateProvesNotExecuted(bool ownerGate, bool unknown)
    {
        int resolutions = 0;
        using var handler = new WireHandler { RejectMutation = true, ExactOwnerGate = ownerGate };
        var provider = new McpRepositoryProvider(Options((_, _) =>
        { resolutions++; return new UserHostConnection(Endpoint, Credential, "generation"); }), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Commit));
        Assert.False(result.Ok);
        Assert.Equal(1, handler.Mutations);
        Assert.Equal(unknown, result.ResultUnknown);
        Assert.Equal(2, resolutions); // integration probe and one mutation connection
    }

    [Fact]
    public async Task RedirectIsRejectedWithoutForwardingToAnotherEndpoint()
    {
        using var handler = new WireHandler { RedirectAll = true };
        using var connection = new ProviderConnection(Options((_, _) => new UserHostConnection(Endpoint, Credential, "generation")), new Factory(handler));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        await Assert.ThrowsAsync<UserHostConnectionException>(() => connection.Client.SendAsync(request));
        Assert.Single(handler.OwnerCredentials);
        using var other = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:54322/mcp");
        await Assert.ThrowsAsync<UserHostConnectionException>(() => connection.Client.SendAsync(other));
        Assert.Single(handler.OwnerCredentials);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DynamicLifecycleKeepsUnknownAndDoesNotReplay(bool ownerGate, bool unknown)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var handler = new WireHandler { RejectMutation = true, ExactOwnerGate = ownerGate };
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
            new Dictionary<string, string[]> { ["buckettie_release_publish"] = ["release.publish"] });
        var provider = new McpLifecycleProvider(Options((_, _) => new UserHostConnection(Endpoint, Credential, "generation")), new Factory(handler), fixture.Issuer, capability: capability);
        var request = new LifecycleRequest("Fixture", "fixture", null, LifecycleAction.ReleasePublish, "1.0", null, null, null,
            ProjectId: fixture.Context.Project, RepositoryUrl: "https://bitbucket.org/example/repo.git");
        LifecycleResult result = await provider.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.Equal(unknown, result.ResultUnknown);
        Assert.Equal(1, handler.Mutations);
    }

    [Fact]
    public async Task ResolverFailureNeverUsesStaticEndpointOrSendsHttp()
    {
        using var handler = new WireHandler();
        var provider = new McpRepositoryProvider(Options((_, _) => throw new UserHostConnectionException("host_profile_mismatch")), new Factory(handler));
        RepositoryProviderResult result = await provider.ExecuteAsync(Request(RepositoryOperation.Status));
        Assert.Equal("host_profile_mismatch", result.ErrorCode);
        Assert.Empty(handler.OwnerCredentials);
    }

    private static McpRepositoryProviderOptions Options(Func<string, string, UserHostConnection> resolve) => new("buckettie", null, "bitbucket")
    { ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = Path.GetFullPath("runtime-fixture"), ConnectionResolver = resolve };

    private static RepositoryProviderRequest Request(RepositoryOperation operation) => new("Fixture", "fixture", "https://bitbucket.org/example/repo.git", null, operation, null, null,
        OperationId: Guid.NewGuid().ToString("N"));

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }

    private sealed class WireHandler : HttpMessageHandler
    {
        public bool RejectAll { get; init; }
        public bool RedirectAll { get; init; }
        public bool RejectMutation { get; init; }
        public bool ExactOwnerGate { get; init; }
        public int Mutations { get; private set; }
        public List<string> OwnerCredentials { get; } = [];
        public List<string?> Assertions { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            OwnerCredentials.Add(request.Headers.GetValues("X-Buckettie-Session").Single());
            if (RedirectAll) return new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("http://127.0.0.1:54322/mcp") } };
            if (RejectAll) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-user-host", version = "1" } };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                if (tool is "bitbucket_repository_commit" or "buckettie_release_publish")
                {
                    Mutations++;
                    if (RejectMutation) return new(HttpStatusCode.Unauthorized) { Content = new StringContent(ExactOwnerGate
                        ? "{\"error\":{\"code\":\"user_host_credential_invalid\",\"outcome\":\"not_executed\"}}" : "{}", Encoding.UTF8, "application/json") };
                }
                if (request.Headers.Authorization is not null) Assertions.Add(request.Headers.Authorization.Parameter);
                object data = tool == "bitbucket_provider_capabilities" ? new { authentication = new { integration_mode = "moyai" } } : (object)new { reflection = Credential };
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data } };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
