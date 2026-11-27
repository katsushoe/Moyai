using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class BuckettieDiffTransportTests
{
    private const string DiffText = "diff --git a/日本語.txt b/日本語.txt\n--- a/日本語.txt\n+++ b/日本語.txt\n@@ -1 +1 @@\n-前\r\n+\uFEFF後\r\n@@ -5 +5 @@\n-旧\n+新\n";

    [Theory]
    [InlineData("supported", "non-origin", "non-origin", null, 2)]
    [InlineData("absent", "non-origin", null, null, 2)]
    [InlineData("wrong-version", "non-origin", null, null, 2)]
    [InlineData("wrong-mode", "non-origin", null, null, 2)]
    [InlineData("string-version", "non-origin", null, null, 2)]
    [InlineData("malformed", "non-origin", null, null, 2)]
    [InlineData("failure", "non-origin", null, null, 2)]
    [InlineData("supported", null, null, null, 1)]
    [InlineData("supported", "", null, null, 1)]
    [InlineData("supported", " ", null, null, 1)]
    [InlineData("supported", "missing", "missing", "provider_remote_not_found", 2)]
    [InlineData("supported", "mismatch", "mismatch", "provider_remote_mismatch", 2)]
    [InlineData("supported", "ambiguous", "ambiguous", "provider_remote_ambiguous", 2)]
    public async Task DiffPreservesSignedContextRemoteAndPayload(string capability, string? remote, string? expectedRemote, string? error, int calls)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Provider = "buckettie", Repository = "bitbucket.org/example/repo", Tool = "bitbucket_repository_diff", Scopes = ["repository.read"] };
        using var handler = new Handler(fixture, context, capability, error);
        var authority = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = ["repository.read"], ["bitbucket_provider_capabilities"] = ["repository.read"] });
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler), fixture.Issuer, authority);
        var request = new RepositoryProviderRequest("Display", "unused", "https://bitbucket.org/example/repo.git", remote, RepositoryOperation.Diff, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered");

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(error is null, result.Ok);
        Assert.Equal(error, result.ErrorCode);
        Assert.False(result.ResultUnknown);
        Assert.Equal(calls, handler.Tools.Count);
        Assert.Equal("bitbucket_repository_diff", handler.Tools[^1]);
        Assert.Equal(1, handler.DiffCalls);
        JsonElement arguments = handler.Arguments!.Value;
        Assert.Equal("registered", arguments.GetProperty("repository").GetString());
        Assert.Equal(expectedRemote is null ? 1 : 2, arguments.EnumerateObject().Count());
        Assert.Equal(expectedRemote is not null, arguments.TryGetProperty("remote", out JsonElement sentRemote));
        if (expectedRemote is not null) Assert.Equal(expectedRemote, sentRemote.GetString());
        if (error is null)
        {
            using JsonDocument response = JsonDocument.Parse(result.Output!);
            Assert.Equal(DiffText, response.RootElement.GetProperty("data").GetProperty("diff").GetString());
        }
        else Assert.DoesNotContain("origin", result.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task DiffWithoutReadScopeDoesNotContactProvider()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Provider = "buckettie", Tool = "bitbucket_repository_diff", Scopes = ["repository.read"] };
        using var handler = new Handler(fixture, context, "supported", null);
        var authority = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = ["repository.commit"] });
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler), fixture.Issuer, authority);
        var result = await provider.ExecuteAsync(new("Display", "unused", "https://github.com/example/repo.git", "non-origin", RepositoryOperation.Diff, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true));

        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Empty(handler.Tools);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(AssertionFixture fixture, AssertionContext context, string capability, string? error) : HttpMessageHandler
    {
        public List<string> Tools { get; } = [];
        public JsonElement? Arguments { get; private set; }
        public int DiffCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "diff-fixture", version = "1" } };
            else
            {
                Assert.Equal("tools/call", method);
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                Tools.Add(tool);
                Assert.Contains(tool, new[] { "bitbucket_provider_capabilities", context.Tool });
                var expected = context with { Tool = tool };
                string assertion = Assert.IsType<string>(request.Headers.Authorization?.Parameter);
                await fixture.Validator(expected).ValidateAsync(assertion, expected, cancellationToken);
                JsonElement arguments = root.GetProperty("params").GetProperty("arguments");
                Assert.Equal("registered", arguments.GetProperty("repository").GetString());
                object payload;
                if (tool == "bitbucket_provider_capabilities")
                {
                    Assert.Single(arguments.EnumerateObject());
                    using JsonDocument capabilities = JsonDocument.Parse(capability switch
                    {
                        "supported" => """{"ok":true,"data":{"remote_resolution":{"version":1,"mode":"repository_url"}}}""",
                        "wrong-version" => """{"ok":true,"data":{"remote_resolution":{"version":2,"mode":"repository_url"}}}""",
                        "wrong-mode" => """{"ok":true,"data":{"remote_resolution":{"version":1,"mode":"other"}}}""",
                        "string-version" => """{"ok":true,"data":{"remote_resolution":{"version":"1","mode":"repository_url"}}}""",
                        "malformed" => """{"ok":true,"data":"invalid"}""",
                        "failure" => """{"ok":false,"error":{"code":"temporarily_unavailable"}}""",
                        _ => """{"ok":true,"data":{}}""",
                    });
                    payload = capabilities.RootElement.Clone();
                }
                else
                {
                    Arguments = arguments.Clone();
                    DiffCalls++;
                    payload = error is null ? (object)new { ok = true, data = new { diff = DiffText } }
                        : new { ok = false, error = new { code = error, outcome = "not_executed" } };
                }
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = payload };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
