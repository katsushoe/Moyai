using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class LifecycleProviderTransportTests
{
    [Theory]
    [InlineData("githubbie", "github", "artifact_path", true, LifecycleAction.ReleaseCreate, "release_create")]
    [InlineData("githubbie", "github", "artifact_path", true, LifecycleAction.ReleasePublish, "release_publish")]
    [InlineData("buckettie", "buckettie", "artifactPath", false, LifecycleAction.ReleaseCreate, "release_create")]
    [InlineData("buckettie", "buckettie", "artifactPath", false, LifecycleAction.ReleasePublish, "release_publish")]
    public async Task ReleaseOperationsUseProviderSpecificContract(string name, string prefix, string artifactProperty, bool includesProject, LifecycleAction action, string operation)
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri("http://localhost/mcp"), prefix), new ClientFactory(client));
        var request = new LifecycleRequest("Moyai", "source", null, action, "1.2.2", "artifact.msi", "notes", null);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal($"{prefix}_{operation}", handler.CalledTool);
        JsonElement arguments = handler.Arguments!.Value;
        Assert.Equal("Moyai", arguments.GetProperty("repository").GetString());
        Assert.Equal("1.2.2", arguments.GetProperty("version").GetString());
        Assert.Equal("notes", arguments.GetProperty("notes").GetString());
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
        Assert.Equal("provider_not_found", result.ErrorCode);
        Assert.Contains("release_not_found", result.ErrorMessage);
    }

    [Fact]
    public async Task GithubReleaseCreatePassesEveryArtifact()
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));
        var request = new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleaseCreate, "1.2.3", "installer.msi", "notes", null, ["installer.msi", "checksums.txt"]);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal(["installer.msi", "checksums.txt"], handler.Arguments!.Value.GetProperty("assets").EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public async Task GithubReleasePublishUsesStableReleaseId()
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));
        var request = new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleasePublish, "1.2.3", "installer.msi", "notes", null, ["installer.msi"], 383033902);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal("github_release_update", handler.CalledTool);
        Assert.Equal(383033902, handler.Arguments!.Value.GetProperty("release_id").GetInt64());
        Assert.False(handler.Arguments.Value.GetProperty("draft").GetBoolean());
        Assert.False(handler.Arguments.Value.TryGetProperty("artifact_path", out _));
    }

    [Theory]
    [InlineData("githubbie", "github", "{\"ok\":true,\"data\":{\"tag\":\"v1.2.3\",\"draft\":false,\"id\":42,\"assets\":[{\"name\":\"artifact.msi\"}]}}")]
    [InlineData("buckettie", "buckettie", "{\"ok\":true,\"data\":{\"version\":\"1.2.3\",\"state\":\"published\",\"artifact_name\":\"artifact.msi\",\"notes\":\"notes\"}}")]
    public async Task ExistingPublishedReleaseIsReturnedAsCompleted(string name, string prefix, string existingRelease)
    {
        using var handler = new ProviderHandler(existingRelease: existingRelease);
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri("http://localhost/mcp"), prefix), new ClientFactory(client));
        var request = new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleaseCreate, "1.2.3", "artifact.msi", "notes", null, ["artifact.msi"]);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.True(result.AlreadyCompleted);
        string calledTool = Assert.IsType<string>(handler.CalledTool);
        Assert.EndsWith("release_get", calledTool, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingGithubDraftReturnsStableReleaseId()
    {
        const string existing = "{\"ok\":true,\"data\":{\"tag\":\"v1.2.3\",\"draft\":true,\"id\":42,\"assets\":[{\"name\":\"artifact.msi\"}]}}";
        using var handler = new ProviderHandler(existingRelease: existing);
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));

        LifecycleResult result = await provider.ExecuteAsync(new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleaseCreate, "1.2.3", "artifact.msi", null, null, ["artifact.msi"]));

        Assert.True(result.Ok);
        Assert.False(result.AlreadyCompleted);
        Assert.Equal(42, result.ResourceId);
    }

    [Fact]
    public async Task ExistingReleaseArtifactMismatchReturnsDiagnosticConflict()
    {
        const string existing = "{\"ok\":true,\"data\":{\"tag\":\"v1.2.3\",\"draft\":false,\"id\":42,\"assets\":[{\"name\":\"other.msi\"}]}}";
        using var handler = new ProviderHandler(existingRelease: existing);
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));

        LifecycleResult result = await provider.ExecuteAsync(new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleaseCreate, "1.2.3", "artifact.msi", null, null, ["artifact.msi"]));

        Assert.False(result.Ok);
        Assert.Equal("provider_conflict", result.ErrorCode);
        Assert.Contains("artifacts", result.ErrorMessage);
    }

    [Fact]
    public async Task ExistingReleaseCommitMismatchReturnsDiagnosticConflict()
    {
        const string existing = "{\"ok\":true,\"data\":{\"tag\":\"v1.2.3\",\"draft\":false,\"id\":42,\"assets\":[]}}";
        const string tag = "{\"ok\":true,\"data\":{\"target_commit_sha\":\"other\"}}";
        using var handler = new ProviderHandler(existingRelease: existing, tagResult: tag);
        using var client = new HttpClient(handler);
        var provider = new McpLifecycleProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new ClientFactory(client));

        LifecycleResult result = await provider.ExecuteAsync(new LifecycleRequest("Moyai", "source", null, LifecycleAction.ReleaseCreate, "1.2.3", null, null, null, CommitHash: "expected"));

        Assert.False(result.Ok);
        Assert.Equal("provider_conflict", result.ErrorCode);
        Assert.Contains("commit", result.ErrorMessage);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ProviderHandler(bool ok = true, string? existingRelease = null, string? tagResult = null) : HttpMessageHandler
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
                string calledTool = root.GetProperty("params").GetProperty("name").GetString()
                    ?? throw new InvalidOperationException("Tool name is required.");
                CalledTool = calledTool;
                Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                string payload;
                if (calledTool.EndsWith("release_get", StringComparison.Ordinal))
                {
                    payload = existingRelease ?? JsonSerializer.Serialize(new { ok = false, data = (object?)null, error = new { code = "release_not_found", message = "Release was not found." } });
                }
                else if (calledTool.EndsWith("tag_get", StringComparison.Ordinal))
                {
                    payload = tagResult ?? JsonSerializer.Serialize(new { ok = true, data = new { target_commit_sha = "expected", target_hash = "expected" } });
                }
                else
                {
                    payload = JsonSerializer.Serialize(new { ok, data = ok ? "published" : null, error = ok ? null : new { code = "release_not_found", message = "Release was not found." } });
                }
                result = new { isError = false, content = new[] { new { type = "text", text = payload } } };
            }
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
