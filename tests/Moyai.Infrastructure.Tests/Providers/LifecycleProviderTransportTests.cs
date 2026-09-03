using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class LifecycleProviderTransportTests
{
    [Theory]
    [InlineData("githubbie", "github", "artifact_path", true)]
    [InlineData("buckettie", "buckettie", "artifactPath", false)]
    public async Task ReleasePublishUsesProviderSpecificContract(string name, string prefix, string artifactProperty, bool includesProject)
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri("http://localhost/mcp"), prefix), new ClientFactory(client));
        var request = new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleasePublish, "1.2.2", "artifact.msi", "notes", null);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal($"{prefix}_release_publish", handler.CalledTool);
        JsonElement arguments = handler.Arguments!.Value;
        Assert.Equal("Moyai", arguments.GetProperty("repository").GetString());
        Assert.Equal("artifact.msi", arguments.GetProperty(artifactProperty).GetString());
        Assert.Equal(includesProject, arguments.TryGetProperty("project", out _));
        Assert.False(arguments.TryGetProperty(artifactProperty == "artifact_path" ? "artifactPath" : "artifact_path", out _));
    }

    [Fact]
    public async Task ReleasePublishPropagatesProviderBusinessFailure()
    {
        using var handler = new ProviderHandler(ok: false);
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));

        LifecycleResult result = await provider.ExecuteAsync(new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleasePublish, "1.2.2", null, null, null));

        Assert.False(result.Ok);
        Assert.Equal("provider_operation_failed", result.ErrorCode);
        Assert.Contains("release_not_found", result.ErrorMessage);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ProviderHandler(bool ok = true) : HttpMessageHandler
    {
        public string? CalledTool { get; private set; }
        public JsonElement? Arguments { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-provider", version = "1.0" } };
            else
            {
                CalledTool = root.GetProperty("params").GetProperty("name").GetString();
                Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                string payload = JsonSerializer.Serialize(new { ok, data = ok ? "published" : null, error = ok ? null : new { code = "release_not_found", message = "Release was not found." } });
                result = new { isError = false, content = new[] { new { type = "text", text = payload } } };
            }
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
