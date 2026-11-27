using System.Text.Json;
using System.Text.Json.Serialization;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

internal sealed record FixtureCase(string CaseId, string Operation, bool ExpectedOk, string? ExpectedErrorCode = null, string? Message = null,
    string? Branch = null, string? ExpectedHead = null, string? Patch = null, FixtureSettings? ExpectedSettings = null,
    string? ExpectedBranch = null, bool? ExpectedClean = null, string[]? ExpectedChangedPaths = null,
    Dictionary<string, string>? ExpectedUnchangedRefs = null, Dictionary<string, string>? ExpectedFileSha256 = null,
    string? ExpectedProviderErrorCode = null)
{
    internal RepositoryOperation Validate()
    {
        if (string.IsNullOrWhiteSpace(CaseId) || !Enum.TryParse(Operation, false, out RepositoryOperation operation)
            || operation is not (RepositoryOperation.Commit or RepositoryOperation.CommitSelected or RepositoryOperation.Push or RepositoryOperation.BranchSwitch or RepositoryOperation.SettingsGet))
            throw new InvalidDataException("Invalid fixture case.");
        if (operation == RepositoryOperation.SettingsGet)
        {
            if (!ExpectedOk || ExpectedErrorCode is not null || ExpectedSettings is null) throw new InvalidDataException("Expected settings are required.");
            FixtureSettings.Parse(JsonSerializer.SerializeToElement(ExpectedSettings));
            return operation;
        }
        if (!FixtureValidation.IsObjectId(ExpectedHead) || string.IsNullOrWhiteSpace(ExpectedBranch) || ExpectedClean is null)
            throw new InvalidDataException("Expected HEAD, branch and clean state are required.");
        if (operation is RepositoryOperation.Commit or RepositoryOperation.CommitSelected)
        {
            if (!ExpectedOk || ExpectedErrorCode is not null || string.IsNullOrWhiteSpace(Message)
                || ExpectedChangedPaths is not { Length: > 0 } || ExpectedChangedPaths.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Commit expectations are required.");
            if (operation == RepositoryOperation.CommitSelected && string.IsNullOrWhiteSpace(Patch)) throw new InvalidDataException("Selected patch is required.");
        }
        if (operation == RepositoryOperation.Push && (ExpectedOk || ExpectedErrorCode != "provider_policy_rejected" || ExpectedProviderErrorCode != "protected_branch"))
            throw new InvalidDataException("Only the protected-push refusal case is supported.");
        if (operation == RepositoryOperation.BranchSwitch && (!ExpectedOk || ExpectedErrorCode is not null || string.IsNullOrWhiteSpace(Branch) || Branch != ExpectedBranch))
            throw new InvalidDataException("An explicit existing target branch is required.");
        return operation;
    }
}

internal sealed record FixtureRoute(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("destination")] string Destination);

internal sealed record FixtureSettings(
    [property: JsonPropertyName("direct_push_branches")] string[] DirectPushBranches,
    [property: JsonPropertyName("protected_branches")] string[] ProtectedBranches,
    [property: JsonPropertyName("pull_branches")] string[] PullBranches,
    [property: JsonPropertyName("tag_pattern")] string TagPattern,
    [property: JsonPropertyName("tag_target_branch")] string TagTargetBranch,
    [property: JsonRequired, JsonPropertyName("require_clean_working_tree")] bool RequireCleanWorkingTree,
    [property: JsonRequired, JsonPropertyName("commit_author_name")] string? CommitAuthorName,
    [property: JsonRequired, JsonPropertyName("commit_author_email")] string? CommitAuthorEmail,
    [property: JsonPropertyName("pull_request_routes")] FixtureRoute[] PullRequestRoutes)
{
    internal static FixtureSettings Parse(JsonElement root)
    {
        BuckettieUserHostResolver.ValidateProperties(root);
        string[] names = ["direct_push_branches", "protected_branches", "pull_branches", "tag_pattern", "tag_target_branch",
            "require_clean_working_tree", "commit_author_name", "commit_author_email", "pull_request_routes"];
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Length
            || names.Any(name => !root.TryGetProperty(name, out _))) throw new JsonException("Expected exactly nine public settings.");
        var routes = new List<FixtureRoute>();
        foreach (JsonElement route in root.GetProperty("pull_request_routes").EnumerateArray())
        {
            if (route.EnumerateObject().Count() != 2) throw new JsonException("Invalid route.");
            routes.Add(new(Text(route, "source"), Text(route, "destination")));
        }
        return new(Array(root, "direct_push_branches"), Array(root, "protected_branches"), Array(root, "pull_branches"),
            Text(root, "tag_pattern"), Text(root, "tag_target_branch"), root.GetProperty("require_clean_working_tree").GetBoolean(),
            root.GetProperty("commit_author_name").GetString(), root.GetProperty("commit_author_email").GetString(), routes.ToArray());
    }

    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() ?? throw new JsonException("Null setting.");
    private static string[] Array(JsonElement root, string name) => root.GetProperty(name).EnumerateArray()
        .Select(value => value.GetString() ?? throw new JsonException("Null branch.")).ToArray();
}

internal sealed record FixtureCheck(bool Passed, string? ErrorCode, string[] MismatchedFields, string? ReturnedCommitSha = null,
    string? ProviderErrorCode = null);

internal sealed record FixtureEvidence(FixtureCase Input, Guid ProjectId, string RepositoryId, DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc, bool ProviderOk, string? ProviderErrorCode, bool ResultUnknown, FixtureCheck Check);

internal sealed record FixtureObservation(int SchemaVersion, string CaseId, Guid ProjectId, string RepositoryId, DateTimeOffset ObservedAtUtc,
    string Branch, string Head, string? ParentHead, bool? Clean, string[]? ChangedPaths, int? CommitCount, int? NetworkOperations,
    Dictionary<string, string>? Refs = null, Dictionary<string, string>? FileSha256 = null, string? Message = null);

internal static class FixtureValidation
{
    internal static bool IsObjectId(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    internal static void RequireWindow(JsonElement ready, DateTimeOffset now)
    {
        DateTimeOffset start = ready.GetProperty("ready_at_utc").GetDateTimeOffset();
        DateTimeOffset expiry = ready.GetProperty("expires_at_utc").GetDateTimeOffset();
        if (ready.GetProperty("ready").ValueKind != JsonValueKind.True || start > now || expiry <= now.AddSeconds(65))
            throw new InvalidDataException("A fresh ready window with more than65 seconds remaining is required.");
    }

    internal static FixtureCheck Inspect(FixtureCase input, RepositoryProviderResult result)
    {
        if (result.ResultUnknown || result.Ok != input.ExpectedOk || result.ErrorCode != input.ExpectedErrorCode)
            return new(false, result.ResultUnknown ? "provider_result_unknown" : "unexpected_provider_result", []);
        RepositoryOperation operation = input.Validate();
        if (operation == RepositoryOperation.Push)
        {
            try
            {
                using JsonDocument detail = JsonDocument.Parse(result.ErrorMessage ?? "null");
                BuckettieUserHostResolver.ValidateProperties(detail.RootElement);
                string? code = detail.RootElement.GetProperty("error").GetProperty("code").GetString();
                return code == input.ExpectedProviderErrorCode
                    ? new(true, null, [], ProviderErrorCode: code)
                    : new(false, "provider_error_code_mismatch", ["provider_error_code"]);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or UserHostConnectionException)
            { return new(false, "provider_error_detail_invalid", ["provider_error_code"]); }
        }
        if (operation != RepositoryOperation.SettingsGet && operation is not (RepositoryOperation.Commit or RepositoryOperation.CommitSelected))
            return new(true, null, []);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(result.Output ?? "null");
            BuckettieUserHostResolver.ValidateProperties(doc.RootElement);
            if (doc.RootElement.GetProperty("ok").ValueKind != JsonValueKind.True) throw new JsonException();
            JsonElement data = doc.RootElement.GetProperty("data");
            if (operation != RepositoryOperation.SettingsGet)
            {
                string? sha = data.GetProperty("commit_hash").GetString();
                return IsObjectId(sha) ? new(true, null, [], sha!.ToLowerInvariant()) : new(false, "commit_sha_invalid", []);
            }
            FixtureSettings actual = FixtureSettings.Parse(data.GetProperty("settings"));
            FixtureSettings expected = input.ExpectedSettings!;
            var mismatch = new List<string>();
            void Compare(string name, bool equal) { if (!equal) mismatch.Add(name); }
            Compare("direct_push_branches", actual.DirectPushBranches.SequenceEqual(expected.DirectPushBranches, StringComparer.Ordinal));
            Compare("protected_branches", actual.ProtectedBranches.SequenceEqual(expected.ProtectedBranches, StringComparer.Ordinal));
            Compare("pull_branches", actual.PullBranches.SequenceEqual(expected.PullBranches, StringComparer.Ordinal));
            Compare("tag_pattern", actual.TagPattern == expected.TagPattern);
            Compare("tag_target_branch", actual.TagTargetBranch == expected.TagTargetBranch);
            Compare("require_clean_working_tree", actual.RequireCleanWorkingTree == expected.RequireCleanWorkingTree);
            Compare("commit_author_name", actual.CommitAuthorName == expected.CommitAuthorName);
            Compare("commit_author_email", actual.CommitAuthorEmail == expected.CommitAuthorEmail);
            Compare("pull_request_routes", actual.PullRequestRoutes.SequenceEqual(expected.PullRequestRoutes));
            return new(mismatch.Count == 0, mismatch.Count == 0 ? null : "settings_mismatch", mismatch.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or UserHostConnectionException)
        { return new(false, "provider_response_invalid", []); }
    }

    internal static FixtureCheck Verify(FixtureEvidence evidence, FixtureObservation observation)
    {
        if (!evidence.Check.Passed || evidence.ResultUnknown || evidence.ProviderOk != evidence.Input.ExpectedOk
            || evidence.ProviderErrorCode != evidence.Input.ExpectedErrorCode || observation.SchemaVersion != 1
            || observation.CaseId != evidence.Input.CaseId || observation.ProjectId != evidence.ProjectId || observation.RepositoryId != evidence.RepositoryId
            || observation.ObservedAtUtc < evidence.CompletedAtUtc)
            return new(false, "observation_identity_or_outcome_invalid", []);
        RepositoryOperation operation = evidence.Input.Validate();
        bool commit = operation is RepositoryOperation.Commit or RepositoryOperation.CommitSelected;
        return VerifyState(evidence, observation, commit ? evidence.Check.ReturnedCommitSha : evidence.Input.ExpectedHead);
    }

    internal static FixtureCheck VerifyLostResponse(FixtureEvidence evidence, FixtureObservation observation)
    {
        if (!evidence.Check.Passed || !evidence.ResultUnknown || evidence.ProviderOk
            || evidence.Input.Validate() != RepositoryOperation.Commit || !evidence.Input.ExpectedOk
            || !evidence.Input.CaseId.StartsWith("owner-response-loss-", StringComparison.Ordinal)
            || observation.SchemaVersion != 1 || observation.CaseId != evidence.Input.CaseId
            || observation.ProjectId != evidence.ProjectId || observation.RepositoryId != evidence.RepositoryId
            || observation.ObservedAtUtc < evidence.CompletedAtUtc || observation.Message != evidence.Input.Message
            || !IsObjectId(observation.Head) || observation.Head == evidence.Input.ExpectedHead)
            return new(false, "lost_response_observation_invalid", []);
        return VerifyState(evidence, observation, observation.Head);
    }

    private static FixtureCheck VerifyState(FixtureEvidence evidence, FixtureObservation observation, string? expectedHead)
    {
        RepositoryOperation operation = evidence.Input.Validate();
        bool commit = operation is RepositoryOperation.Commit or RepositoryOperation.CommitSelected;
        var mismatch = new List<string>();
        if (!IsObjectId(observation.Head) || !string.Equals(observation.Head, expectedHead, StringComparison.OrdinalIgnoreCase)) mismatch.Add("head");
        if (observation.Branch != evidence.Input.ExpectedBranch) mismatch.Add("branch");
        if (observation.Clean != evidence.Input.ExpectedClean) mismatch.Add("clean");
        if (observation.NetworkOperations != 0) mismatch.Add("network_operations");
        if (observation.CommitCount != (commit ? 1 : 0)) mismatch.Add("commit_count");
        if (commit)
        {
            if (!string.Equals(observation.ParentHead, evidence.Input.ExpectedHead, StringComparison.OrdinalIgnoreCase)) mismatch.Add("parent_head");
            if (observation.ChangedPaths is null || !observation.ChangedPaths.Order(StringComparer.Ordinal)
                .SequenceEqual(evidence.Input.ExpectedChangedPaths!.Order(StringComparer.Ordinal), StringComparer.Ordinal)) mismatch.Add("changed_paths");
        }
        var expectedRefs = new Dictionary<string, string>(evidence.Input.ExpectedUnchangedRefs ?? [], StringComparer.Ordinal);
        expectedRefs["refs/heads/" + evidence.Input.ExpectedBranch] = expectedHead!;
        if (observation.Refs is null || observation.Refs.Count != expectedRefs.Count
            || expectedRefs.Any(expected => !observation.Refs.TryGetValue(expected.Key, out string? value)
                || !string.Equals(value, expected.Value, StringComparison.OrdinalIgnoreCase))) mismatch.Add("refs");
        foreach (var expected in evidence.Input.ExpectedFileSha256 ?? [])
            if (observation.FileSha256 is null || !observation.FileSha256.TryGetValue(expected.Key, out string? value)
                || !string.Equals(value, expected.Value, StringComparison.OrdinalIgnoreCase)) mismatch.Add("file_sha256");
        return new(mismatch.Count == 0, mismatch.Count == 0 ? null : "independent_state_mismatch", mismatch.Distinct(StringComparer.Ordinal).ToArray(), evidence.Check.ReturnedCommitSha);
    }
}
