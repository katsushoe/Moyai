using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RepositoryProviderTransportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecuteAsyncWhenProviderReturnsBusinessStatusPropagatesIt(bool ok, bool structured)
    {
        using var handler = new ProviderHandler(ok, structured);
        using var client = new HttpClient(handler);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "origin", RepositoryOperation.Status, null, null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(ok, result.Ok);
        Assert.Equal("github_repository_status", handler.CalledTool);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(ok ? null : "provider_not_found", result.ErrorCode);
        string payload = Assert.IsType<string>(ok ? result.Output : result.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(payload);
        Assert.Equal(ok, document.RootElement.GetProperty("ok").GetBoolean());
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ProviderHandler(bool ok, bool structured) : HttpMessageHandler
    {
        private static readonly string[] ProtocolVersions = ["2026-07-28"];

        public string? CalledTool { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover")
            {
                result = new { supportedVersions = ProtocolVersions, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            }
            else if (method == "initialize")
            {
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-provider", version = "1.0" } };
            }
            else
            {
                Assert.Equal("tools/call", method);
                CalledTool = root.GetProperty("params").GetProperty("name").GetString();
                CallCount++;
                string payload = JsonSerializer.Serialize(new { ok, data = ok ? "done" : null, error = ok ? null : new { code = "repository_not_found", correlation_id = "integration-1" } });
                result = structured
                    ? (object)new { isError = false, structuredContent = JsonSerializer.Deserialize<JsonElement>(payload), content = Array.Empty<object>() }
                    : new { isError = false, content = new[] { new { type = "text", text = payload } } };
            }
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
