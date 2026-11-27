using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class RepositorySettingsTransportTests
{
    private static readonly string[] LocalCommitChangedFields = ["local_commit_branches"];
    [Theory]
    [InlineData("[]")]
    [InlineData("[\"codex/docs-main-v1010\",\"develop\"]")]
    public async Task LocalCommitUpdateUsesExactSignedSettingsWriteRoute(string value)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_repository_settings_update", Scopes = ["repository.settings.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true)
        {
            ResponseJson = Providers.RepositorySettingsTests.LocalCommitSuccessJson(value),
        };
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = ["repository.settings.write"] });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "unused", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"local_commit_branches\":" + value + "}")));
        var result = await provider.ExecuteAsync(request);
        Assert.True(result.Ok);
        Assert.Equal(1, handler.SettingsCalls);
        JsonElement arguments = handler.Arguments!.Value;
        Assert.Equal("registered", arguments.GetProperty("repository").GetString());
        Assert.Equal(2, arguments.EnumerateObject().Count());
        JsonElement changes = arguments.GetProperty("changes");
        Assert.Single(changes.EnumerateObject());
        Assert.Equal(value, changes.GetProperty("local_commit_branches").GetRawText());
        var parsed = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsUpdate, result);
        Assert.True(parsed.Ok);
        Assert.True(parsed.Approved);
        Assert.Equal(LocalCommitChangedFields, parsed.ChangedFields);
        Assert.NotNull(parsed.Settings!.LocalCommitBranches);
    }

    [Theory]
    [InlineData(false, "repository.read", "auth_scope_denied")]
    [InlineData(false, "repository.commit", "auth_scope_denied")]
    [InlineData(true, "repository.settings.write", "auth_scope_denied")]
    public async Task LocalCommitUpdateNeverSubstitutesScopeOrLegacy(bool legacy, string scope, string code)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_repository_settings_update", Scopes = ["repository.settings.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true,
            new Dictionary<string, string[]> { [context.Tool] = [scope] });
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var result = await provider.ExecuteAsync(new("Display", "unused", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: !legacy,
            SettingsChanges: RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"local_commit_branches\":[]}"))));
        Assert.False(result.Ok);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task MissingSettingsReadToolMappingFailsBeforeCapabilityOrHttp()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Tool = "github_repository_settings_get", Scopes = ["repository.read"] };
        using var handler = new Handler(fixture.Validator(context), context, true);
        var capability = new AssertionCapability("githubie", "githubie", "1", "ES256", true, new Dictionary<string, string[]>());
        var provider = new McpRepositoryProvider(new("githubbie", new Uri("http://localhost/mcp"), "github"), new Factory(handler), fixture.Issuer, capability);
        var result = await provider.ExecuteAsync(new("Display", "unused", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsGet, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true));
        Assert.False(result.Ok);
        Assert.Equal("provider_capability_missing", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("github", false, false)]
    [InlineData("github", false, true)]
    [InlineData("github", true, false)]
    [InlineData("github", true, true)]
    [InlineData("bitbucket", false, false)]
    [InlineData("bitbucket", false, true)]
    [InlineData("bitbucket", true, false)]
    [InlineData("bitbucket", true, true)]
    public async Task SettingsDispatchRequiresCapabilityAndSignedContext(string prefix, bool update, bool supported)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        RepositoryOperation operation = update ? RepositoryOperation.SettingsUpdate : RepositoryOperation.SettingsGet;
        string providerId = prefix == "github" ? "githubie" : "buckettie";
        AssertionContext context = fixture.Context with { Provider = providerId, Tool = RepositoryProviderContract.ToolName(prefix, operation), Scopes = [RepositoryAssertionPolicy.Scope(operation)] };
        using var handler = new Handler(fixture.Validator(context), context, supported, prefix);
        var capability = new AssertionCapability(providerId, providerId, "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new(providerId, new Uri("http://localhost/mcp"), prefix), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "source", "https://github.com/example/repo.git", null, operation, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: update ? RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"require_clean_working_tree\":false}")) : null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.Equal(supported, result.Ok);
        Assert.Equal(supported ? 1 : 0, handler.SettingsCalls);
        Assert.Equal(supported ? null : "provider_capability_missing", result.ErrorCode);
        if (!supported) Assert.True(handler.Requests > 0);
        if (supported) Assert.Equal("registered", handler.Arguments!.Value.GetProperty("repository").GetString());
        if (supported)
        {
            var typed = RepositorySettingsResult.Parse("Display", operation, result, providerId);
            Assert.True(typed.Ok);
            Assert.Equal(providerId, typed.Provider);
            Assert.Null(typed.Settings!.CommitAuthorName);
            Assert.DoesNotContain("secret-value", JsonSerializer.Serialize(typed));
        }
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

    [Theory]
    [InlineData("github")]
    [InlineData("bitbucket")]
    public async Task LostUpdateResponseIsUnknownAndNotRetried(string prefix)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        string providerId = prefix == "github" ? "githubie" : "buckettie";
        var context = fixture.Context with { Provider = providerId, Tool = prefix + "_repository_settings_update", Scopes = ["repository.settings.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true, prefix) { LoseResponse = true };
        var capability = new AssertionCapability(providerId, providerId, "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new(providerId, new Uri("http://localhost/mcp"), prefix), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "source", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"require_clean_working_tree\":false}")));
        RepositoryProviderResult result = await provider.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.True(result.ResultUnknown);
        Assert.Equal(1, handler.SettingsCalls);
    }

    [Theory]
    [InlineData(false, "standalone", false, false, "provider_integration_mode_mismatch", false)]
    [InlineData(true, "standalone", false, false, "provider_integration_mode_mismatch", false)]
    [InlineData(true, "moyai", true, false, "repository_settings_approval_cancelled", false)]
    [InlineData(true, "moyai", false, true, "provider_invalid_response", true)]
    public async Task BuckettieIntegrationApprovalAndFailureRemainFailClosed(bool update, string mode, bool cancelled, bool unapproved, string code, bool unknown)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var operation = update ? RepositoryOperation.SettingsUpdate : RepositoryOperation.SettingsGet;
        var context = fixture.Context with { Provider = "buckettie", Tool = RepositoryProviderContract.ToolName("bitbucket", operation), Scopes = [RepositoryAssertionPolicy.Scope(operation)] };
        using var handler = new Handler(fixture.Validator(context), context, true, "bitbucket") { IntegrationMode = mode, ErrorCode = cancelled ? "repository_settings_approval_cancelled" : null, Approved = !unapproved };
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() });
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler), fixture.Issuer, capability);
        var request = new RepositoryProviderRequest("Display", "unused", "https://github.com/example/repo.git", null, operation, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: true, ProviderRepositoryId: "registered",
            SettingsChanges: update ? RepositorySettingsChanges.Create(Providers.RepositorySettingsTests.Changes("{\"require_clean_working_tree\":false}")) : null);
        var raw = await provider.ExecuteAsync(request);
        var typed = RepositorySettingsResult.Parse("Display", operation, raw, "buckettie");
        Assert.False(typed.Ok);
        Assert.Equal(code, typed.Error!.Code);
        Assert.Equal(unknown ? "unknown" : "not_executed", typed.Error.Outcome);
        Assert.Equal(mode == "standalone" ? 0 : 1, handler.SettingsCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuckettieWrongScopeAndLegacyNeverContactProvider(bool legacy)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = fixture.Context with { Provider = "buckettie", Tool = "bitbucket_repository_settings_update", Scopes = ["repository.settings.write"] };
        using var handler = new Handler(fixture.Validator(context), context, true, "bitbucket");
        var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true, new Dictionary<string, string[]> { [context.Tool] = ["repository.commit"] });
        var provider = new McpRepositoryProvider(new("buckettie", new Uri("http://localhost/mcp"), "bitbucket"), new Factory(handler), fixture.Issuer, capability);
        var result = await provider.ExecuteAsync(new("Display", "unused", "https://github.com/example/repo.git", null, RepositoryOperation.SettingsUpdate, null, null,
            ProjectId: context.Project, OperationId: context.OperationId, UseAssertion: !legacy));
        Assert.Equal("auth_scope_denied", result.ErrorCode);
        Assert.Equal(0, handler.Requests);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(IAssertionValidator validator, AssertionContext expected, bool supported, string prefix = "github") : HttpMessageHandler
    {
        public string IntegrationMode { get; init; } = "moyai";
        public bool Approved { get; init; } = true;
        public string? ErrorCode { get; init; }
        public bool LoseResponse { get; init; }
        public string? ResponseJson { get; init; }
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
                if (tool == prefix + "_provider_capabilities")
                {
                    object data = prefix == "github" ? new { repository_settings_read = supported, repository_settings_update = supported }
                        : new { operations = new { repository_settings_read = supported, repository_settings_update = supported }, authentication = new { integration_mode = IntegrationMode } };
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = new { ok = true, data } };
                }
                else
                {
                    Assert.Equal(expected.Tool, tool);
                    Arguments = root.GetProperty("params").GetProperty("arguments").Clone();
                    SettingsCalls++;
                    string assertion = Assert.IsType<string>(request.Headers.Authorization?.Parameter);
                    await validator.ValidateAsync(assertion, expected, cancellationToken);
                    if (LoseResponse) throw new HttpRequestException("Response lost after save.");
                    using JsonDocument payload = JsonDocument.Parse(ErrorCode is null ? ResponseJson ?? Providers.RepositorySettingsTests.SuccessJson(Approved)
                        : JsonSerializer.Serialize(new { ok = false, error = new { code = ErrorCode, outcome = "not_executed" } }));
                    result = new { isError = false, content = Array.Empty<object>(), structuredContent = payload.RootElement.Clone() };
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
