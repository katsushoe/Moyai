using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class RepositorySettingsTransportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SettingsDispatchRequiresCapabilityAndSignedContext(bool update, bool supported)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        RepositoryOperation operation = update ? RepositoryOperation.SettingsUpdate : RepositoryOperation.SettingsGet;
        AssertionContext context = fixture.Context with { Tool = RepositoryProviderContract.ToolName("github", operation), Scopes = [RepositoryAssertionPolicy.Scope(operation)] };
        using var handler = new Handler(fixture.Validator(context), context, supported);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "source", "https://github.com/example/repo.git", null, operation, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: update ? RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"require_clean_working_tree\":false}")) : null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(supported, result.Ok);
        Assert.Equal(supported ? 1 : 0, handler.SettingsCalls);
        Assert.Equal(supported ? null : "provider_capability_missing", result.ErrorCode);
        if (supported) Assert.Equal("registered", handler.Arguments!.Value.GetProperty("repository").GetString());
        if (supported && update)
        {
            JsonElement changes = handler.Arguments!.Value.GetProperty("changes");
            Assert.False(changes.GetProperty("require_clean_working_tree").GetBoolean());
            Assert.Single(changes.EnumerateObject());
        }
    }

    [Fact]
    public async Task MissingDedicatedWriteScopeDoesNotContactProvider()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var handler = new Handler(fixture.Validator(), fixture.Context, true);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { ["github_repository_settings_update"] = ["repository.commit"] });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "source", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: fixture.Context.Project, OperationId: "write", UseAssertion: true);
        RepositoryProviderResult result = await provider.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task LostUpdateResponseIsUnknownAndNotRetried()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_repository_settings_update", Scopes = ["repository.settings.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true) { LoseResponse = true };
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "source", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"require_clean_working_tree\":false}")));
        RepositoryProviderResult result = await provider.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, handler.SettingsCalls);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(IAssertionValidator validator, AssertionContext expected, bool supported) : HttpMessageHandler
    {
        public bool LoseResponse { get; init; }
        public int Requests { get; private set; }
        public int SettingsCalls { get; private set; }
        public JsonElement? Arguments { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            object result;
            string? method = root.GetProperty("method").GetString();
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "settings-provider", version = "1.0" } };
            else
            {
                Assert.Equal("tools/call", method);
                string? tool = root.GetProperty("params").GetProperty("name").GetString();
                if (tool == "github_provider_capabilities") result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data = new { repository_settings_read = supported, repository_settings_update = supported } } };
                else
                {
                    Assert.Equal(expected.Tool, tool);
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    SettingsCalls++;
                    string assertion = Assert.IsType<string>(request.Headers.Authorization?.Parameter);
                    await validator.ValidateAsync(assertion, expected, cancellationToken);
                    if (LoseResponse) throw new HttpRequestException("Response lost after save.");
                    using JsonDocument payload = JsonDocument.Parse(Providers.RepositorySettingsTests.SuccessJson(true));
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = payload.RootElement.Clone() };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
