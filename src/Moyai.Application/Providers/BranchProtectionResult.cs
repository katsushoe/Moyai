using System.Text.Json;

namespace Moyai.Application.Providers;

/// <summary>classic ruleの共通項目だけです。Providerの完全なpolicy本文ではありません。</summary>
public sealed record BranchProtectionPolicy(bool RequirePullRequest, int? RequiredApprovingReviews, bool AllowForcePushes,
    bool AllowDeletions, bool EnforceAdmins, bool RequireStatusChecks, string[] StatusCheckContexts, bool? StrictStatusChecks);

/// <summary>読取可否と変更有無を区別した観測です。</summary>
public sealed record BranchProtectionState(string Branch, bool? CanUpdate, bool? Changed, BranchProtectionPolicy? Policy);
/// <summary>秘密値を含めない型付きエラーです。</summary>
public sealed record BranchProtectionError(string Code, string Outcome, bool Retryable, string SuggestedAction, string? CorrelationId = null);

/// <summary>生のProvider応答や診断文を返さない、ブランチ保護の共通結果です。</summary>
public sealed record BranchProtectionResult(bool Ok, string Operation, string Project, string? Provider,
    BranchProtectionState? Protection, BranchProtectionError? Error, string ProtectionKind = "classic_exact_branch")
{
    /// <summary>未実行と結果不明を区別します。</summary>
    public static BranchProtectionResult Failure(string project, string? provider, RepositoryOperation operation, string code, bool unknown = false) =>
        new(false, operation.ContractName(), project, provider, null, new(code, unknown ? "unknown" : "not_executed", false,
            unknown ? "check_status" : "inspect_configuration"));

    /// <summary>Provider応答の共通項目だけを型付きに投影します。</summary>
    public static BranchProtectionResult Parse(string project, string provider, string branch, RepositoryOperation operation, RepositoryProviderResult result)
    {
        bool mutation = operation != RepositoryOperation.BranchProtectionGet;
        if (!result.Ok || result.ResultUnknown)
        {
            string code = SafeCode(result.ErrorCode) ?? "provider_operation_failed";
            string outcome = result.ResultUnknown ? "unknown" : "not_executed";
            string action = result.ResultUnknown ? "check_status" : "inspect_configuration";
            string? correlation = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(result.ErrorMessage ?? "null");
                JsonElement error = document.RootElement.GetProperty("error");
                if (!result.ResultUnknown && error.TryGetProperty("outcome", out var state) && state.ValueKind == JsonValueKind.String
                    && state.GetString() is "not_executed" or "failed" or "pending" or "unknown") outcome = state.GetString()!;
                if (error.TryGetProperty("suggested_action", out var suggested) && suggested.ValueKind == JsonValueKind.String
                    && suggested.GetString() is "check_status" or "verify_permissions" or "configure_authentication" or "inspect_policy" or "retry_later" or "request_approval" or "contact_provider") action = suggested.GetString()!;
                if (error.TryGetProperty("correlation_id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out _)) correlation = id.GetString();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { }
            if (outcome == "unknown") action = "check_status";
            return new(false, operation.ContractName(), project, provider, null, new(code, outcome, false, action, correlation));
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Output ?? "null");
            JsonElement root = document.RootElement;
            if (root.GetProperty("ok").ValueKind != JsonValueKind.True) throw new JsonException();
            if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null) throw new JsonException();
            JsonElement data = root.GetProperty("data");
            if (data.GetProperty("branch").GetString() != branch) throw new JsonException();
            bool? canUpdate = mutation ? null : data.GetProperty("can_update").GetBoolean();
            bool? changed = mutation ? data.GetProperty("changed").GetBoolean() : null;
            JsonElement policy = data.GetProperty("policy");
            BranchProtectionPolicy? common = policy.ValueKind == JsonValueKind.Null ? null : ReadPolicy(policy);
            if (!mutation && (canUpdate == true) != (common is not null)
                || operation == RepositoryOperation.BranchProtectionUpdate && common is null
                || operation == RepositoryOperation.BranchProtectionDelete && common is not null) throw new JsonException();
            return new(true, operation.ContractName(), project, provider, new(branch, canUpdate, changed, common), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return Failure(project, provider, operation, "provider_invalid_response", mutation); }
    }

    private static BranchProtectionPolicy ReadPolicy(JsonElement policy)
    {
        JsonElement reviews = policy.GetProperty("required_pull_request_reviews");
        JsonElement checks = policy.GetProperty("required_status_checks");
        int? count = reviews.ValueKind == JsonValueKind.Null ? null : reviews.GetProperty("required_approving_review_count").GetInt32();
        if (count is < 0 or > 6) throw new JsonException();
        return new(reviews.ValueKind != JsonValueKind.Null, count, policy.GetProperty("allow_force_pushes").GetBoolean(),
            policy.GetProperty("allow_deletions").GetBoolean(), policy.GetProperty("enforce_admins").GetBoolean(), checks.ValueKind != JsonValueKind.Null,
            checks.ValueKind == JsonValueKind.Null ? [] : checks.GetProperty("contexts").EnumerateArray().Select(value => value.GetString() ?? throw new JsonException()).ToArray(),
            checks.ValueKind == JsonValueKind.Null ? null : checks.GetProperty("strict").GetBoolean());
    }

    private static string? SafeCode(string? code) => code is { Length: > 0 and <= 100 }
        && code.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') ? code : null;
}
