using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RepositoryProviderTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedCommitIsDispatchedOnlyWhenProviderAdvertisesSupport(bool supportsSelectedCommit)
    {
        using var handler = new SelectedCommitCapabilityHandler(supportsSelectedCommit);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", null, RepositoryOperation.CommitSelected, "selected", null,
            ExpectedHead: new string('a', 40), Patch: "diff --git a/a b/a");

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(supportsSelectedCommit, result.Ok);
        if (!supportsSelectedCommit) Assert.Equal("provider_capability_missing", result.ErrorCode);
        string[] expectedTools = supportsSelectedCommit
            ? ["github_provider_capabilities", "github_repository_commit_selected"]
            : ["github_provider_capabilities"];
        Assert.Equal(expectedTools, handler.CalledTools);
    }

    [Fact]
    public async Task SelectedCommitPreservesPatchTextAcrossMcpJsonTransport()
    {
        const string patch = "diff --git a/COMMANDS.ja.md b/COMMANDS.ja.md\n--- a/COMMANDS.ja.md\n+++ b/COMMANDS.ja.md\n@@ -1,2 +1,3 @@\n 日本語の説明\n+追加行\n@@ -8,3 +9,2 @@\n-削除行\n 維持行\ndiff --git a/src/Program.cs b/src/Program.cs\nnew file mode 100644\n--- /dev/null\n+++ b/src/Program.cs\n@@ -0,0 +1,2 @@\n+\uFEFFusing System;\n+Console.WriteLine(\"日本語\");\n";
        using var handler = new SelectedCommitCapabilityHandler(true);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", null, RepositoryOperation.CommitSelected, "selected", null,
            ExpectedHead: new string('a', 40), Patch: patch);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(patch, handler.SelectedPatch);
        Assert.Equal(request.ExpectedHead, handler.SelectedExpectedHead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StashIsDispatchedOnlyWhenProviderAdvertisesSupport(bool supportsStash)
    {
        using var handler = new StashCapabilityHandler(supportsStash);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", null, RepositoryOperation.StashPush, "split changes", null,
            Paths: ["src/a.cs"]);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(supportsStash, result.Ok);
        if (!supportsStash) Assert.Equal("provider_capability_missing", result.ErrorCode);
        string[] expectedTools = supportsStash
            ? ["github_provider_capabilities", "github_repository_stash_push"]
            : ["github_provider_capabilities"];
        Assert.Equal(expectedTools, handler.CalledTools);
        Assert.Equal(supportsStash, handler.StashDispatched);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecuteAsyncWhenProviderReturnsBusinessStatusPropagatesIt(bool ok, bool structured)
    {
        using var handler = new ProviderHandler(ok, structured);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", null, RepositoryOperation.Status, null, null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(ok, result.Ok);
        Assert.Equal("github_repository_status", handler.CalledTool);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(ok ? null : "provider_not_found", result.ErrorCode);
        string payload = Assert.IsType<string>(ok ? result.Output : result.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(payload);
        Assert.Equal(ok, document.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData(true, "github-origin-https")]
    [InlineData(false, null)]
    public async Task ExecuteAsyncForwardsRemoteOnlyWhenProviderAdvertisesContract(bool supportsRemoteResolution, string? expectedRemote)
    {
        using var handler = new RemoteResolutionProviderHandler(supportsRemoteResolution);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://localhost/mcp"), "github"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "github-origin-https", RepositoryOperation.Status, null, null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(["github_provider_capabilities", "github_repository_status"], handler.CalledTools);
        Assert.Equal(expectedRemote, handler.StatusRemote);
    }

    [Theory]
    [InlineData("bitbucket", true, "moyai", "bitbucket-origin-https", 3)]
    [InlineData("bitbucket", false, "moyai", null, 3)]
    [InlineData("github", true, "moyai", null, 1)]
    public async Task CommitForwardsRemoteOnlyForSupportedBuckettie(string prefix, bool supported, string mode, string? expectedRemote, int expectedCalls)
    {
        using var handler = new RemoteResolutionProviderHandler(supported, mode);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("test", new Uri("http://localhost/mcp"), prefix), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "bitbucket-origin-https", RepositoryOperation.Commit, "fixture commit", null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(expectedCalls, handler.CalledTools.Count);
        Assert.Equal($"{prefix}_repository_commit", handler.CalledTools[^1]);
        Assert.Equal(expectedRemote, handler.StatusRemote);
        Assert.Equal("fixture commit", handler.CommitMessage);
        Assert.Equal("Moyai", handler.Repository);
        if (prefix == "bitbucket") Assert.Equal(["bitbucket_provider_capabilities", "bitbucket_provider_capabilities", "bitbucket_repository_commit"], handler.CalledTools);
    }

    [Fact]
    public async Task CommitRejectsBuckettieOutsideMoyaiModeBeforeDelegation()
    {
        using var handler = new RemoteResolutionProviderHandler(true, "direct");
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("test", new Uri("http://localhost/mcp"), "bitbucket"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "bitbucket-origin-https", RepositoryOperation.Commit, "fixture commit", null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.False(result.Ok);
        Assert.Equal("provider_integration_mode_mismatch", result.ErrorCode);
        Assert.Equal(["bitbucket_provider_capabilities"], handler.CalledTools);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CommitWithoutRemoteSkipsResolutionCapabilityQuery(string? remote)
    {
        using var handler = new RemoteResolutionProviderHandler(true);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("test", new Uri("http://localhost/mcp"), "bitbucket"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", remote, RepositoryOperation.Commit, "fixture commit", null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(["bitbucket_provider_capabilities", "bitbucket_repository_commit"], handler.CalledTools);
        Assert.Null(handler.StatusRemote);
    }

    [Fact]
    public async Task CommitOmitsRemoteWhenResolutionCapabilityQueryFails()
    {
        using var handler = new RemoteResolutionProviderHandler(true, failRepositoryCapability: true);
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("test", new Uri("http://localhost/mcp"), "bitbucket"), new FreshClientFactory(handler));
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "non-origin", RepositoryOperation.Commit, "fixture commit", null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(["bitbucket_provider_capabilities", "bitbucket_provider_capabilities", "bitbucket_repository_commit"], handler.CalledTools);
        Assert.Null(handler.StatusRemote);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FreshClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
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

    private sealed class StashCapabilityHandler(bool supportsStash) : HttpMessageHandler
    {
        private static readonly string[] ProtocolVersions = ["2026-07-28"];
        public List<string> CalledTools { get; } = [];
        public bool StashDispatched { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover")
                result = new { supportedVersions = ProtocolVersions, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize")
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-provider", version = "1.0" } };
            else
            {
                Assert.Equal("tools/call", method);
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                CalledTools.Add(tool);
                object payload;
                if (tool == "github_provider_capabilities")
                    payload = new { ok = true, data = new { repository_stash_push = supportsStash } };
                else
                {
                    StashDispatched = tool == "github_repository_stash_push";
                    payload = new { ok = StashDispatched, data = "done" };
                }
                string jsonPayload = JsonSerializer.Serialize(payload);
                result = new { isError = false, content = new[] { new { type = "text", text = jsonPayload } } };
            }
            string response = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SelectedCommitCapabilityHandler(bool supportsSelectedCommit) : HttpMessageHandler
    {
        private static readonly string[] ProtocolVersions = ["2026-07-28"];
        public List<string> CalledTools { get; } = [];
        public string? SelectedPatch { get; private set; }
        public string? SelectedExpectedHead { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = ProtocolVersions, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-provider", version = "1.0" } };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                CalledTools.Add(tool);
                if (tool == "github_repository_commit_selected")
                {
                    JsonElement arguments = root.GetProperty("params").GetProperty("arguments");
                    SelectedPatch = arguments.GetProperty("patch").GetString();
                    SelectedExpectedHead = arguments.GetProperty("expected_head").GetString();
                }
                object payload = tool == "github_provider_capabilities"
                    ? new { ok = true, data = new { operations = new { repository_commit_selected = supportsSelectedCommit } } }
                    : new { ok = true, data = "done" };
                result = new { isError = false, content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload) } } };
            }
            string response = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class RemoteResolutionProviderHandler(bool supportsRemoteResolution, string integrationMode = "moyai", bool failRepositoryCapability = false) : HttpMessageHandler
    {
        private static readonly string[] ProtocolVersions = ["2026-07-28"];
        public List<string> CalledTools { get; } = [];
        public string? StatusRemote { get; private set; }
        public string? CommitMessage { get; private set; }
        public string? Repository { get; private set; }

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
                JsonElement parameters = root.GetProperty("params");
                string tool = parameters.GetProperty("name").GetString()!;
                CalledTools.Add(tool);
                if (tool is "github_provider_capabilities" or "bitbucket_provider_capabilities")
                {
                    object? remoteResolution = supportsRemoteResolution ? new { version = 1, mode = "repository_url" } : null;
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { remote_resolution = remoteResolution, authentication = new { integration_mode = integrationMode } } } };
                    if (failRepositoryCapability && parameters.GetProperty("arguments").TryGetProperty("repository", out _))
                        result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = false, error = new { code = "temporarily_unavailable", retryable = true } } };
                }
                else
                {
                    JsonElement arguments = parameters.GetProperty("arguments");
                    StatusRemote = arguments.TryGetProperty("remote", out JsonElement remote) ? remote.GetString() : null;
                    CommitMessage = arguments.TryGetProperty("message", out JsonElement message) ? message.GetString() : null;
                    Repository = arguments.GetProperty("repository").GetString();
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { } } };
                }
            }
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
