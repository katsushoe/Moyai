using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class LifecycleAssertionTests
{
    private static readonly Dictionary<string, string[]> GithubieScopes = new(StringComparer.Ordinal)
    {
        ["github_release_get"] = ["repository.read"],
        ["github_release_create"] = ["release.publish", "artifact.upload"],
    };

    [Fact]
    public async Task GithubieReleaseCreateSendsDistinctValidatedAssertionPerTool()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        using var handler = new GithubieReleaseHandler(f, GithubieScopes);
        LifecycleResult result = await Provider(f, handler, GithubieScopes).ExecuteAsync(Request(f));
        Assert.True(result.Ok, result.ErrorCode);
        Assert.Equal(["github_release_get", "github_release_create"], handler.Tools);
        Assert.Equal(2, handler.Assertions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(handler.Assertions, assertion => Assert.DoesNotContain(assertion, result.Output ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GithubieReleaseToolWithoutConfiguredScopeIsNotCalled()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        var readOnly = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["github_release_get"] = ["repository.read"] };
        using var handler = new GithubieReleaseHandler(f, readOnly);
        LifecycleResult result = await Provider(f, handler, readOnly).ExecuteAsync(Request(f));
        Assert.False(result.Ok);
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.Equal(["github_release_get"], handler.Tools);
    }

    [Fact]
    public async Task GithubieReleaseWithoutRepositoryUrlFailsClosed()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        using var handler = new GithubieReleaseHandler(f, GithubieScopes);
        LifecycleResult result = await Provider(f, handler, GithubieScopes).ExecuteAsync(Request(f) with { RepositoryUrl = null });
        Assert.False(result.Ok);
        Assert.Equal("auth_project_mismatch", result.ErrorCode);
        Assert.Empty(handler.Tools);
    }

    private static McpLifecycleProvider Provider(AssertionFixture f, HttpMessageHandler handler, Dictionary<string, string[]> scopes) =>
        new(new McpRepositoryProviderOptions("githubbie", new Uri("http://127.0.0.1:54321/mcp"), "github"), new TestClientFactory(handler), f.Issuer,
            new SqliteAssertionAudit(f.Database, f.Clock), new AssertionCapability("githubie", "githubie", "1", "ES256", true, scopes));

    private static LifecycleRequest Request(AssertionFixture f) =>
        new("Test", "unused", null, LifecycleAction.ReleaseCreate, "1.0.0", null, "notes", null, ProjectId: f.Context.Project, RepositoryUrl: "https://github.com/example/repo.git");

    private sealed class TestClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class GithubieReleaseHandler(AssertionFixture fixture, IReadOnlyDictionary<string, string[]> scopes) : HttpMessageHandler
    {
        public List<string> Tools { get; } = [];
        public List<string> Assertions { get; } = [];

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
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                string assertion = request.Headers.Authorization!.Parameter!;
                string operationId = request.Headers.GetValues("X-Moyai-Operation-Id").Single();
                var context = new AssertionContext("githubie", fixture.Context.Project, "github.com/example/repo", tool, scopes[tool], operationId);
                await fixture.Validator(context).ValidateAsync(assertion, context, cancellationToken);
                Tools.Add(tool);
                Assertions.Add(assertion);
                object structured = tool == "github_release_get"
                    ? new { ok = false, error = new { code = "release_not_found" } }
                    : new { ok = true, data = new { id = 1, draft = true } };
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = structured };
            }
            else if (method == "server/discover")
            {
                Assert.Null(request.Headers.Authorization);
                result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            }
            else
            {
                Assert.Null(request.Headers.Authorization);
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "isolated-githubie", version = "1.0" } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
