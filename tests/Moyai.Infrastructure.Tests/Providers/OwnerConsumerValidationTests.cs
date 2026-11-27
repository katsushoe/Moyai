using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Application.Providers;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class OwnerConsumerValidationTests
{
    private static readonly string Parent = new('a', 40);
    private static readonly string Child = new('b', 40);
    private static readonly Guid Project = Guid.Parse("21b89191-7ed1-4479-85a8-6faf98c4e2d1");
    private static readonly DateTimeOffset Completed = DateTimeOffset.Parse("2026-10-26T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string SettingsJson = """
        {"direct_push_branches":["develop"],"protected_branches":["main"],"pull_branches":["develop","main"],
        "tag_pattern":"^v.*$","tag_target_branch":"main","require_clean_working_tree":true,
        "commit_author_name":"Fixture Author","commit_author_email":"fixture@example.invalid",
        "pull_request_routes":[{"source":"develop","destination":"main"}]}
        """;

    [Fact]
    public void AllNineSettingsAreComparedAndOnlyFixedFieldNamesAreReported()
    {
        FixtureCheck check = FixtureValidation.Inspect(SettingsCase(), SettingsResult(JsonNode.Parse(SettingsJson)!));
        Assert.True(check.Passed);
        Assert.Empty(check.MismatchedFields);
    }

    [Theory]
    [InlineData("require_clean_working_tree")]
    [InlineData("commit_author_name")]
    [InlineData("commit_author_email")]
    public void MissingExpectedBooleanOrNullableAuthorIsNotReplacedByDefault(string field)
    {
        JsonObject expected = JsonNode.Parse(SettingsJson)!.AsObject();
        expected.Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixtureSettings>(expected.ToJsonString()));
    }

    [Theory]
    [InlineData("direct_push_branches")]
    [InlineData("protected_branches")]
    [InlineData("pull_branches")]
    [InlineData("tag_pattern")]
    [InlineData("tag_target_branch")]
    [InlineData("require_clean_working_tree")]
    [InlineData("commit_author_name")]
    [InlineData("commit_author_email")]
    [InlineData("pull_request_routes")]
    public void EveryPublicSettingMismatchFails(string field)
    {
        JsonNode settings = JsonNode.Parse(SettingsJson)!;
        settings[field] = field switch
        {
            "direct_push_branches" or "protected_branches" or "pull_branches" => new JsonArray("other"),
            "require_clean_working_tree" => JsonValue.Create(false),
            "pull_request_routes" => JsonNode.Parse("[{\"source\":\"other\",\"destination\":\"main\"}]"),
            _ => JsonValue.Create("untrusted reflection value"),
        };
        FixtureCheck result = FixtureValidation.Inspect(SettingsCase(), SettingsResult(settings));
        Assert.False(result.Passed);
        Assert.Equal("settings_mismatch", result.ErrorCode);
        Assert.Equal(new[] { field }, result.MismatchedFields);
        Assert.DoesNotContain("reflection", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("extra")]
    [InlineData("null-array")]
    [InlineData("null-route")]
    [InlineData("unknown-route-field")]
    public void InvalidSettingsCannotPassThroughDefaults(string kind)
    {
        JsonObject settings = JsonNode.Parse(SettingsJson)!.AsObject();
        switch (kind)
        {
            case "missing": settings.Remove("require_clean_working_tree"); break;
            case "wrong-type": settings["require_clean_working_tree"] = "true"; break;
            case "extra": settings["token"] = "reflection fixture"; break;
            case "null-array": settings["pull_branches"] = null; break;
            case "null-route": settings["pull_request_routes"] = new JsonArray((JsonNode?)null); break;
            default: settings["pull_request_routes"] = JsonNode.Parse("[{\"source\":\"develop\",\"destination\":\"main\",\"credential\":\"fixture\"}]"); break;
        }
        FixtureCheck result = FixtureValidation.Inspect(SettingsCase(), SettingsResult(settings));
        Assert.False(result.Passed);
        Assert.Equal("provider_response_invalid", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("refs/heads/main")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void CommitRequiresTheReturnedFullObjectId(string? hash)
    {
        string output = JsonSerializer.Serialize(new { ok = true, data = new { commit_hash = hash } });
        FixtureCheck result = FixtureValidation.Inspect(CommitCase(), new(true, "repository_commit", output, null, null));
        Assert.False(result.Passed);
        Assert.Equal("commit_sha_invalid", result.ErrorCode);
    }

    [Fact]
    public void ReturnedShaMustMatchIndependentHeadParentBranchAndFileState()
    {
        FixtureEvidence evidence = Evidence();
        FixtureCheck result = FixtureValidation.Verify(evidence, Observation());
        Assert.True(result.Passed);
        Assert.Equal(Child, result.ReturnedCommitSha);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("parent")]
    [InlineData("branch")]
    [InlineData("dirty")]
    [InlineData("paths")]
    [InlineData("count")]
    [InlineData("network")]
    [InlineData("refs")]
    [InlineData("files")]
    public void IndependentMismatchNeverAcceptsTheMutation(string kind)
    {
        FixtureObservation observation = Observation();
        observation = kind switch
        {
            "head" => observation with { Head = Parent },
            "parent" => observation with { ParentHead = Child },
            "branch" => observation with { Branch = "develop" },
            "dirty" => observation with { Clean = false },
            "paths" => observation with { ChangedPaths = ["unselected.txt"] },
            "count" => observation with { CommitCount = 2 },
            "network" => observation with { NetworkOperations = 1 },
            "refs" => observation with { Refs = new() { ["refs/heads/develop"] = Child } },
            _ => observation with { FileSha256 = null },
        };
        Assert.False(FixtureValidation.Verify(Evidence(), observation).Passed);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("project")]
    [InlineData("repository")]
    [InlineData("stale")]
    [InlineData("unknown")]
    public void StaleWrongIdentityAndUnknownEvidenceCannotBeCleared(string kind)
    {
        FixtureObservation observation = Observation();
        FixtureEvidence evidence = Evidence();
        switch (kind)
        {
            case "case": observation = observation with { CaseId = "other" }; break;
            case "project": observation = observation with { ProjectId = Guid.NewGuid() }; break;
            case "repository": observation = observation with { RepositoryId = "other" }; break;
            case "stale": observation = observation with { ObservedAtUtc = Completed.AddSeconds(-1) }; break;
            default: evidence = evidence with { ResultUnknown = true }; break;
        }
        Assert.False(FixtureValidation.Verify(evidence, observation).Passed);
    }

    [Theory]
    [InlineData(64, true, false)]
    [InlineData(65, true, false)]
    [InlineData(66, true, true)]
    [InlineData(120, false, false)]
    public void ExpiredShortAndStoppedWindowsRefuseBeforeDispatch(int remaining, bool ready, bool accepted)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { ready, ready_at_utc = Completed.AddSeconds(-5), expires_at_utc = Completed.AddSeconds(remaining) }));
        if (accepted) FixtureValidation.RequireWindow(document.RootElement, Completed);
        else Assert.Throws<InvalidDataException>(() => FixtureValidation.RequireWindow(document.RootElement, Completed));
    }

    [Fact]
    public void PendingCaseBlocksOtherCasesAndItsReservationCannotBeReplayed()
    {
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "moyai-owner-journal-");
        string directory = Path.GetFullPath(prefix + Guid.NewGuid().ToString("N"));
        if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test directory.");
        Directory.CreateDirectory(directory);
        try
        {
            var journal = new FixtureJournal(directory);
            journal.Reserve("first");
            Assert.Throws<InvalidOperationException>(() => journal.Reserve("second"));
            Assert.Throws<InvalidOperationException>(() => journal.Complete("second"));
            journal.RequirePending("first");
            journal.Complete("first");
            Assert.Throws<IOException>(() => journal.Reserve("first"));
            journal.Reserve("second");
            journal.RequirePending("second");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"protected_branch\"}}", true)]
    [InlineData("{\"error\":{\"code\":\"repository_not_allowed\"}}", false)]
    [InlineData("{\"error\":{\"code\":\"forbidden\"}}", false)]
    [InlineData("{\"error\":{\"code\":null}}", false)]
    [InlineData("{\"error\":{\"code\":\"protected_branch\",\"code\":\"forbidden\"}}", false)]
    [InlineData("protected_branch", false)]
    [InlineData(null, false)]
    public void PushRefusalRequiresBothCommonCodeAndStructuredProviderReason(string? detail, bool accepted)
    {
        var input = new FixtureCase("push-fixture", "Push", false, "provider_policy_rejected", ExpectedHead: Parent,
            ExpectedBranch: "main", ExpectedClean: true, ExpectedProviderErrorCode: "protected_branch");
        var result = new RepositoryProviderResult(false, "push", null, "provider_policy_rejected", detail);
        FixtureCheck check = FixtureValidation.Inspect(input, result);
        Assert.Equal(accepted, check.Passed);
        if (accepted) Assert.Equal("protected_branch", check.ProviderErrorCode);
        Assert.False(FixtureValidation.Inspect(input, result with { ErrorCode = "protected_branch" }).Passed);
        Assert.False(FixtureValidation.Inspect(input, result with { ResultUnknown = true }).Passed);
    }

    [Fact]
    public void OldPushCaseCannotConfuseProviderReasonWithCommonCode()
    {
        var input = new FixtureCase("old-push", "Push", false, "protected_branch", ExpectedHead: Parent,
            ExpectedBranch: "main", ExpectedClean: true);
        Assert.Throws<InvalidDataException>(() => input.Validate());
    }

    private static FixtureCase SettingsCase()
    {
        using JsonDocument document = JsonDocument.Parse(SettingsJson);
        return new("settings-fixture", "SettingsGet", true, ExpectedSettings: FixtureSettings.Parse(document.RootElement));
    }
    private static RepositoryProviderResult SettingsResult(JsonNode settings) => new(true, "repository_settings_get",
        new JsonObject { ["ok"] = true, ["data"] = new JsonObject { ["settings"] = settings } }.ToJsonString(), null, null);
    private static FixtureCase CommitCase() => new("commit-fixture", "Commit", true, Message: "fixture commit", ExpectedHead: Parent,
        ExpectedBranch: "main", ExpectedClean: true, ExpectedChangedPaths: ["README.txt"],
        ExpectedUnchangedRefs: new() { ["refs/heads/develop"] = Parent }, ExpectedFileSha256: new() { ["unselected.txt"] = new string('c', 64) });
    private static FixtureEvidence Evidence() => new(CommitCase(), Project, "fixture", Completed.AddSeconds(-5), Completed, true, null, false,
        FixtureValidation.Inspect(CommitCase(), new(true, "repository_commit", JsonSerializer.Serialize(new { ok = true, data = new { commit_hash = Child } }), null, null)));
    private static FixtureObservation Observation() => new(1, "commit-fixture", Project, "fixture", Completed.AddSeconds(1), "main", Child,
        Parent, true, ["README.txt"], 1, 0, new() { ["refs/heads/develop"] = Parent, ["refs/heads/main"] = Child }, new() { ["unselected.txt"] = new string('c', 64) });
}
