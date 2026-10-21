using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RepositorySettingsTests
{
    private static readonly string[] DevelopBranches = ["develop"];
    private static readonly string[] MainBranches = ["main"];
    private static readonly string[] CleanChangedFields = ["require_clean_working_tree"];
    private static readonly string[] LocalCommitChangedFields = ["local_commit_branches"];
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"secret\"}")]
    [InlineData("{\"require_clean_working_tree\":null}")]
    [InlineData("{\"require_clean_working_tree\":\"false\"}")]
    [InlineData("{\"pull_branches\":[1]}")]
    [InlineData("{\"commit_author_name\":\"author\"}")]
    [InlineData("{\"commit_author_email\":\"author@example.invalid\"}")]
    [InlineData("{\"tag_pattern\":\"\"}")]
    [InlineData("{\"commit_author_name\":\"Test\",\"commit_author_email\":\"not-an-email\"}")]
    public void InvalidChangesAreRejected(string json) => Assert.Throws<ArgumentException>(() => RepositorySettingsChanges.Create(Changes(json)));

    [Fact]
    public void ExplicitFalseAndEmptyArrayArePreservedWithoutDefaults()
    {
        var changes = RepositorySettingsChanges.Create(Changes("{\"require_clean_working_tree\":false,\"pull_branches\":[]}"));
        var request = new RepositoryProviderRequest("Display", "source", "url", null, RepositoryOperation.SettingsUpdate, null, null, ProviderRepositoryId: "registered", SettingsChanges: changes);
        var arguments = RepositoryProviderContract.Arguments("github", request);
        Assert.Equal("registered", arguments["repository"]);
        Assert.Equal(2, changes.Values.Count);
        Assert.False(changes.Values["require_clean_working_tree"].GetBoolean());
        Assert.Empty(changes.Values["pull_branches"].EnumerateArray());
        Assert.False(changes.Values.ContainsKey("protected_branches"));
        Assert.Equal("github_repository_settings_update", RepositoryProviderContract.ToolName("github", request.Operation));
        Assert.Equal("repository.settings.write", RepositoryAssertionPolicy.Scope(request.Operation));
        Assert.Equal("repository.read", RepositoryAssertionPolicy.Scope(RepositoryOperation.SettingsGet));
    }

    [Theory]
    [InlineData("{\"ok\":true,\"data\":{\"repository_settings_read\":true}}", true)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_settings_get\":true}}", false)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_settings_read\":false}}", false)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_settings_read\":\"true\"}}", false)]
    [InlineData("invalid", false)]
    public void CapabilityUsesExactReadKey(string json, bool supported) => Assert.Equal(supported, RepositoryProviderContract.SupportsSettings(json, RepositoryOperation.SettingsGet));

    [Fact]
    public void SafeResultDropsUnexpectedProviderFieldsAndRequiresApproval()
    {
        string json = SuccessJson(false);
        var get = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsGet, new(true, "repository_settings_get", json, null, null));
        Assert.True(get.Ok);
        Assert.Null(get.Settings!.CommitAuthorName);
        Assert.DoesNotContain("secret-value", JsonSerializer.Serialize(get));
        var update = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsUpdate, new(true, "repository_settings_update", json, null, null));
        Assert.False(update.Ok);
        Assert.Equal("unknown", update.Error!.Outcome);
    }

    [Theory]
    [InlineData("github", "githubie")]
    [InlineData("bitbucket", "buckettie")]
    public async Task RoutingUsesMappedIdAndOnlyExplicitChanges(string provider, string canonical)
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync(provider);
        var updated = await routing.ExecuteSettingsAsync("Display", Changes("{\"commit_author_name\":\"Test Author\",\"commit_author_email\":\"test@example.invalid\"}"));
        Assert.True(updated.Ok);
        Assert.True(updated.Approved);
        Assert.Equal(canonical, updated.Provider);
        Assert.Equal("registered", fixture.Provider.Last!.RepositoryId);
        Assert.True(fixture.Provider.Last.UseAssertion);
        Assert.Equal(2, fixture.Provider.Last.SettingsChanges!.Values.Count);
        Assert.Null(fixture.Provider.Last.ServiceToken);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("other", false, "provider_capability_missing")]
    [InlineData("github", true, "auth_scope_denied")]
    [InlineData("bitbucket", true, "auth_scope_denied")]
    public async Task UnconfirmedProviderAndLegacyAreNotDispatched(string provider, bool legacy, string code)
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync(provider, legacy);
        var result = await routing.ExecuteSettingsAsync("Display");
        Assert.False(result.Ok);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("provider_unavailable")]
    [InlineData("auth_assertion_expired")]
    public async Task UnknownUpdateDoesNotRetryAndAllowsSeparateRead(string code)
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync();
        fixture.Provider.Next = new(false, "repository_settings_update", null, code, "secret transport detail", true);
        var result = await routing.ExecuteSettingsAsync("Display", Changes("{\"require_clean_working_tree\":false}"));
        Assert.False(result.Ok);
        Assert.Equal("unknown", result.Error!.Outcome);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.DoesNotContain("secret transport detail", JsonSerializer.Serialize(result));
        fixture.Provider.Next = new(true, "repository_settings_get", SuccessJson(true), null, null);
        Assert.True((await routing.ExecuteSettingsAsync("Display")).Ok);
        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("repository_settings_invalid")]
    [InlineData("repository_settings_approval_cancelled")]
    [InlineData("repository_settings_write_failed")]
    public void SettingsErrorsRemainStructured(string code) => Assert.Equal(code, RepositoryProviderContract.NormalizeErrorCode(JsonSerializer.Serialize(new { error = new { code } })));

    [Theory]
    [InlineData("{\"data\":{\"operations\":{\"repository_settings_read\":true}}}", true)]
    [InlineData("{\"data\":{\"operations\":{\"repository_settings_get\":true}}}", false)]
    [InlineData("{\"data\":{\"repository_settings_read\":true,\"operations\":{\"repository_settings_read\":false}}}", false)]
    [InlineData("{\"data\":{\"operations\":{\"repository_settings_read\":\"true\"}}}", false)]
    [InlineData("{\"data\":null}", false)]
    public void BuckettieCapabilityUsesNestedExactReadKey(string json, bool supported) =>
        Assert.Equal(supported, RepositoryProviderContract.SupportsSettings(json, RepositoryOperation.SettingsGet, "bitbucket"));

    [Fact]
    public void BuckettieUpdatePreservesFalseEmptyArrayAndOnlyTwoArguments()
    {
        var changes = RepositorySettingsChanges.Create(Changes("{\"require_clean_working_tree\":false,\"pull_branches\":[]}"));
        var request = new RepositoryProviderRequest("Display", "unused", "unused", "non-origin", RepositoryOperation.SettingsUpdate, null, null, ProviderRepositoryId: "registered", SettingsChanges: changes);
        var arguments = RepositoryProviderContract.Arguments("bitbucket", request);
        Assert.Equal("bitbucket_repository_settings_update", RepositoryProviderContract.ToolName("bitbucket", request.Operation));
        Assert.Equal(2, arguments.Count);
        Assert.Equal("registered", arguments["repository"]);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, JsonElement>>(arguments["changes"]);
        Assert.False(values["require_clean_working_tree"].GetBoolean());
        Assert.Empty(values["pull_branches"].EnumerateArray());
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public async Task ApprovalCancellationKeepsErrorCodeAndSafeCorrelation()
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync();
        const string correlation = "3f3f4669f3544474a57f293f22eb070b";
        fixture.Provider.Next = new(false, "repository_settings_update", null, "repository_settings_approval_cancelled",
            JsonSerializer.Serialize(new { error = new { correlation_id = correlation, secret = "hidden" } }));
        var result = await routing.ExecuteSettingsAsync("Display", Changes("{\"require_clean_working_tree\":false}"));
        Assert.False(result.Ok);
        Assert.False(result.Approved);
        Assert.Equal("repository_settings_approval_cancelled", result.Error!.Code);
        Assert.Equal("not_executed", result.Error.Outcome);
        Assert.Equal(correlation, result.Error.CorrelationId);
        Assert.DoesNotContain("hidden", JsonSerializer.Serialize(result));
        Assert.Equal(1, fixture.Provider.Calls);
    }

    public static Dictionary<string, JsonElement> Changes(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"develop\"")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    [InlineData("[\" \"]")]
    public void LocalCommitBranchesRejectInvalidInput(string value) =>
        Assert.Throws<ArgumentException>(() => RepositorySettingsChanges.Create(Changes("{\"local_commit_branches\":" + value + "}")));

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"codex/docs-main-v1010\",\"develop\",\"Develop\",\"develop\"]")]
    public void LocalCommitBranchesPreserveExactArrayAndOnlyExplicitField(string value)
    {
        var changes = RepositorySettingsChanges.Create(Changes("{\"local_commit_branches\":" + value + "}"));
        var request = new RepositoryProviderRequest("Display", "source", "url", null, RepositoryOperation.SettingsUpdate, null, null, ProviderRepositoryId: "registered", SettingsChanges: changes);
        var arguments = RepositoryProviderContract.Arguments("github", request);
        Assert.Equal(2, arguments.Count);
        Assert.Equal("registered", arguments["repository"]);
        var sent = Assert.IsAssignableFrom<IReadOnlyDictionary<string, JsonElement>>(arguments["changes"]);
        Assert.Single(sent);
        Assert.Equal(value, sent["local_commit_branches"].GetRawText());
        Assert.False(sent.ContainsKey("direct_push_branches"));
    }

    [Fact]
    public void LocalCommitBranchesFieldNameIsCaseSensitive() =>
        Assert.Throws<ArgumentException>(() => RepositorySettingsChanges.Create(Changes("{\"Local_Commit_Branches\":[]}")));

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"codex/docs-main-v1010\",\"develop\"]")]
    public async Task GithubieLocalCommitUpdateRetainsProjectBindingAndApproval(string value)
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync();
        fixture.Provider.Next = new(true, "repository_settings_update", LocalCommitSuccessJson(value), null, null);
        var result = await routing.ExecuteSettingsAsync("Display", Changes("{\"local_commit_branches\":" + value + "}"));
        Assert.True(result.Ok);
        Assert.True(result.Approved);
        Assert.Equal(LocalCommitChangedFields, result.ChangedFields);
        Assert.NotNull(result.Settings!.LocalCommitBranches);
        Assert.True(fixture.Provider.Last!.UseAssertion);
        Assert.NotEqual(Guid.Empty, fixture.Provider.Last.ProjectId);
        Assert.Equal("registered", fixture.Provider.Last.RepositoryId);
        Assert.Single(fixture.Provider.Last.SettingsChanges!.Values);
        Assert.Null(fixture.Provider.Last.ServiceToken);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Fact]
    public async Task BuckettieLocalCommitExtensionIsRejectedBeforeDispatch()
    {
        await using var fixture = new Fixture();
        ProviderRoutingService routing = await fixture.CreateAsync("bitbucket");
        var result = await routing.ExecuteSettingsAsync("Display", Changes("{\"local_commit_branches\":[\"develop\"]}"));
        Assert.False(result.Ok);
        Assert.Equal("repository_settings_invalid", result.Error!.Code);
        Assert.Equal("not_executed", result.Error.Outcome);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("githubie", true)]
    [InlineData("githubbie", true)]
    [InlineData("buckettie", false)]
    public void LocalCommitGetIsOptionalAndGithubieOnly(string provider, bool exposed)
    {
        var result = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsGet,
            new(true, "repository_settings_get", LocalCommitSuccessJson("[]"), null, null), provider);
        Assert.True(result.Ok);
        Assert.Equal(exposed, result.Settings!.LocalCommitBranches is not null);
        if (exposed) Assert.Empty(result.Settings.LocalCommitBranches!);
        using var output = JsonDocument.Parse(JsonSerializer.Serialize(result.Settings));
        Assert.Equal(exposed, output.RootElement.TryGetProperty("local_commit_branches", out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"develop\"")]
    [InlineData("[1]")]
    public void MalformedLocalCommitOutputIsRejected(string value)
    {
        string json = LocalCommitSuccessJson(value);
        var result = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsUpdate, new(true, "repository_settings_update", json, null, null));
        Assert.False(result.Ok);
        Assert.Equal("provider_invalid_response", result.Error!.Code);
        Assert.Equal("unknown", result.Error.Outcome);
    }

    [Fact]
    public void BuckettieLocalCommitChangedFieldRemainsInvalid()
    {
        var result = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsUpdate,
            new(true, "repository_settings_update", LocalCommitSuccessJson("[]"), null, null), "buckettie");
        Assert.False(result.Ok);
        Assert.Equal("provider_invalid_response", result.Error!.Code);
    }

    [Fact]
    public void LocalCommitChangedFieldRequiresReturnedRegisteredValue()
    {
        string json = SuccessJson(true).Replace("require_clean_working_tree\"]", "local_commit_branches\"]", StringComparison.Ordinal);
        var result = RepositorySettingsResult.Parse("Display", RepositoryOperation.SettingsUpdate, new(true, "repository_settings_update", json, null, null));
        Assert.False(result.Ok);
        Assert.Equal("unknown", result.Error!.Outcome);
    }

    public static string LocalCommitSuccessJson(string value, bool approved = true)
    {
        var data = System.Text.Json.Nodes.JsonNode.Parse(SuccessJson(approved))!["data"]!;
        data["settings"]!["local_commit_branches"] = System.Text.Json.Nodes.JsonNode.Parse(value);
        data["changed_fields"] = new System.Text.Json.Nodes.JsonArray("local_commit_branches");
        return new System.Text.Json.Nodes.JsonObject { ["ok"] = true, ["data"] = data.DeepClone() }.ToJsonString();
    }

    public static string SuccessJson(bool approved) => JsonSerializer.Serialize(new
    {
        ok = true,
        data = new
        {
            settings = new { direct_push_branches = DevelopBranches, pull_branches = DevelopBranches, protected_branches = MainBranches, tag_target_branch = "main", tag_pattern = "v.*", require_clean_working_tree = false, commit_author_name = (string?)null, commit_author_email = (string?)null, token = "secret-value" },
            changed_fields = CleanChangedFields,
            approved,
        },
    });

    private sealed class Provider : IRepositoryProvider
    {
        public string Name { get; set; } = "githubbie";
        public int Calls { get; private set; }
        public RepositoryProviderRequest? Last { get; private set; }
        public RepositoryProviderResult Next { get; set; } = new(true, "repository_settings_update", SuccessJson(true), null, null);
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(Next);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "moyai-settings-" + Guid.NewGuid().ToString("N"));
        public Provider Provider { get; } = new();
        public async Task<ProviderRoutingService> CreateAsync(string provider = "github", bool legacy = false)
        {
            Directory.CreateDirectory(_root);
            var db = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "data.db"), Pooling = false }.ToString());
            await new SqliteDatabaseInitializer(db).InitializeAsync();
            var repository = new SqliteProjectRepository(db);
            var projects = new ProjectService(repository, TimeProvider.System);
            await projects.CreateAsync(new CreateProjectCommand("Display", "source", null, "https://github.com/example/repo.git", provider));
            await projects.SetProviderRepositoryAsync("Display", 1, "registered", false, "test", "test");
            Provider.Name = provider == "bitbucket" ? "buckettie" : "githubbie";
            return new(repository, new SqliteServiceTokenRepository(db), [Provider], TimeProvider.System,
                legacy ? new RepositoryAuthentication("legacy", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)) : null);
        }
        public ValueTask DisposeAsync() { Directory.Delete(_root, true); return ValueTask.CompletedTask; }
    }
}
