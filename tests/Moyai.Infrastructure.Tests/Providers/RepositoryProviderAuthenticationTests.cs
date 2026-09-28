using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.Infrastructure.Tests.Authentication;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RepositoryProviderAuthenticationTests
{
    [Fact]
    public async Task BootstrapCapabilityOutsideToolScopesIsCalledWithoutAssertion()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        using var handler = new ProviderHandler(HttpStatusCode.OK);
        RepositoryProviderResult result = await Adapter(f, handler).ExecuteAsync(Request(f, RepositoryOperation.ProviderCapabilities));
        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(["bitbucket_provider_capabilities"], handler.Tools);
        Assert.All(handler.Authorizations, Assert.Null);
    }

    [Fact]
    public async Task ProviderAuthenticationRejectionIsDistinguishedFromTransportFailure()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        using var handler = new ProviderHandler(HttpStatusCode.Unauthorized);
        RepositoryProviderResult result = await Adapter(f, handler).ExecuteAsync(Request(f, RepositoryOperation.Status));
        Assert.False(result.Ok);
        Assert.Equal("provider_authentication_rejected", result.ErrorCode);
        Assert.Contains("401", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static McpRepositoryProvider Adapter(AssertionFixture f, HttpMessageHandler handler) =>
        new(new McpRepositoryProviderOptions("buckettie", new Uri("http://127.0.0.1:54321/mcp"), "bitbucket"), new TestClientFactory(handler), f.Issuer,
            new AssertionCapability("buckettie", "buckettie", "1", "ES256", true, new Dictionary<string, string[]> { ["bitbucket_repository_status"] = ["repository.read"] }),
            new SqliteAssertionAudit(f.Database, f.Clock));

    private static RepositoryProviderRequest Request(AssertionFixture f, RepositoryOperation operation) =>
        new("Test", "unused", "https://bitbucket.org/example/repo.git", "origin", operation, null, null,
            ProjectId: f.Context.Project, OperationId: Guid.NewGuid().ToString("N"), UseAssertion: true);

    private sealed class TestClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class ProviderHandler(HttpStatusCode toolStatus) : HttpMessageHandler
    {
        public List<string> Tools { get; } = [];
        public List<string?> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "tools/call")
            {
                Tools.Add(root.GetProperty("params").GetProperty("name").GetString()!);
                Authorizations.Add(request.Headers.Authorization?.Parameter);
                if (toolStatus != HttpStatusCode.OK) return new HttpResponseMessage(toolStatus);
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { provider = "bitbucket" } } };
            }
            else if (method == "server/discover")
            {
                result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            }
            else
            {
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "isolated-buckettie", version = "1.0" } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
