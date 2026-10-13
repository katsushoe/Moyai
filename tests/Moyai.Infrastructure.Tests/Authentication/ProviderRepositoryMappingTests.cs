using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class ProviderRepositoryMappingTests
{
    [Theory]
    [InlineData("githubbie", "githubie", "github", "https://github.com/example/neon_sdk.git", true)]
    [InlineData("buckettie", "buckettie", "bitbucket", "https://bitbucket.org/example/neon_sdk.git", true)]
    [InlineData("githubbie", "githubie", "github", "https://github.com/example/neon_sdk.git", false)]
    [InlineData("buckettie", "buckettie", "bitbucket", "https://bitbucket.org/example/neon_sdk.git", false)]
    public async Task PersistedMappingUsesRegisteredIdWithUnchangedSignedProjectAndRepository(string routingName, string audience, string prefix, string url, bool correctMapping)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var repository = new SqliteProjectRepository(fixture.Database);
        var projects = new ProjectService(repository, fixture.Clock);
        var original = await projects.CreateAsync(new CreateProjectCommand("Neon_SDK", "source", null, url, routingName));
        string mapping = correctMapping ? "neonsdk" : "another-repository";
        await projects.SetProviderRepositoryAsync(original.Name, original.Revision, mapping, false, "test", "mapper");
        AssertionContext registered = fixture.Context with
        {
            Project = original.Id,
            Provider = audience,
            Repository = RepositoryAssertionPolicy.NormalizeRepository(url),
            Tool = prefix + "_repository_status",
            Scopes = ["repository.read"],
        };
        using var handler = new MappingHandler(fixture.Validator(registered), registered);
        var capability = new AssertionCapability(audience, audience, "1", "ES256", true,
            new Dictionary<string, string[]> { [registered.Tool] = ["repository.read"] });
        var adapter = new McpRepositoryProvider(new McpRepositoryProviderOptions(routingName, new Uri("http://localhost/mcp"), prefix),
            new ClientFactory(handler), fixture.Issuer, capability);
        var routing = new ProviderRoutingService(repository, new SqliteServiceTokenRepository(fixture.Database), [adapter], fixture.Clock);

        RepositoryProviderResult result = await routing.ExecuteAsync("Neon_SDK", RepositoryOperation.Status);

        Assert.Equal(mapping, handler.RepositoryArgument);
        Assert.Equal(1, handler.ToolCalls);
        Assert.Equal(correctMapping, result.Ok);
        Assert.Equal(correctMapping ? null : "provider_authentication_rejected", result.ErrorCode);
        Assert.Equal(correctMapping ? 1 : 0, handler.Executions);
        var loaded = await projects.GetAsync("Neon_SDK");
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(url, loaded.RepositoryUrl);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MappingHandler(IAssertionValidator validator, AssertionContext registered) : HttpMessageHandler
    {
        public string? RepositoryArgument { get; private set; }
        public int ToolCalls { get; private set; }
        public int Executions { get; private set; }

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
                Assert.Null(request.Headers.Authorization);
                result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            }
            else if (method == "initialize")
            {
                Assert.Null(request.Headers.Authorization);
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "mapped-provider", version = "1.0" } };
            }
            else
            {
                Assert.Equal("tools/call", method);
                Assert.Equal(registered.Tool, root.GetProperty("params").GetProperty("name").GetString());
                RepositoryArgument = root.GetProperty("params").GetProperty("arguments").GetProperty("repository").GetString();
                ToolCalls++;
                string assertion = Assert.IsType<string>(request.Headers.Authorization?.Parameter);
                // The Provider resolves its own registered binding from the supplied ID.
                AssertionContext expected = registered with
                {
                    OperationId = request.Headers.GetValues("X-Moyai-Operation-Id").Single(),
                    Project = RepositoryArgument == "neonsdk" ? registered.Project : Guid.NewGuid(),
                };
                try { await validator.ValidateAsync(assertion, expected, cancellationToken); }
                catch (ProviderAuthenticationException) { return new HttpResponseMessage(HttpStatusCode.Unauthorized); }
                Executions++;
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { repository = RepositoryArgument } } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
