using System.Text.Json.Serialization;

namespace Moyai.Application.Providers;

/// <summary>明示変更だけを指定します。nullはProviderの既存設定を保持します。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchProtectionEdit(
    [property: JsonPropertyName("require_pull_request")] bool? RequirePullRequest = null,
    [property: JsonPropertyName("required_approving_reviews")] int? RequiredApprovingReviews = null,
    [property: JsonPropertyName("allow_force_pushes")] bool? AllowForcePushes = null,
    [property: JsonPropertyName("allow_deletions")] bool? AllowDeletions = null,
    [property: JsonPropertyName("enforce_admins")] bool? EnforceAdmins = null,
    [property: JsonPropertyName("require_status_checks")] bool? RequireStatusChecks = null,
    [property: JsonPropertyName("status_check_contexts")] string[]? StatusCheckContexts = null,
    [property: JsonPropertyName("strict_status_checks")] bool? StrictStatusChecks = null)
{
    /// <summary>Provider契約に一致しない空・範囲外・矛盾入力を拒否します。</summary>
    public void Validate()
    {
        if (RequirePullRequest is null && RequiredApprovingReviews is null && AllowForcePushes is null && AllowDeletions is null
            && EnforceAdmins is null && RequireStatusChecks is null && StatusCheckContexts is null && StrictStatusChecks is null)
            throw new ArgumentException("At least one explicit edit is required.");
        if (RequiredApprovingReviews is < 0 or > 6 || RequirePullRequest == false && RequiredApprovingReviews is not null
            || RequireStatusChecks == false && (StatusCheckContexts is not null || StrictStatusChecks is not null)
            || RequireStatusChecks == true && StatusCheckContexts is null)
            throw new ArgumentException("Contradictory protection edit.");
        if (StatusCheckContexts is not null && (StatusCheckContexts.Length > 100
            || StatusCheckContexts.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl))
            || StatusCheckContexts.Distinct(StringComparer.Ordinal).Count() != StatusCheckContexts.Length))
            throw new ArgumentException("Invalid status-check contexts.");
    }

    /// <summary>wildcardを含まないexact branch名だけを受理します。</summary>
    public static void ValidateBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 1024 || branch == "@" || branch.StartsWith('/') || branch.EndsWith('/')
            || branch.EndsWith('.') || branch.Contains("..", StringComparison.Ordinal) || branch.Contains("@{", StringComparison.Ordinal)
            || branch.Any(character => char.IsControl(character) || character is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            || branch.Split('/').Any(part => part.Length == 0 || part.StartsWith('.') || part.EndsWith(".lock", StringComparison.Ordinal)))
            throw new ArgumentException("An exact valid branch name is required.", nameof(branch));
    }
}
