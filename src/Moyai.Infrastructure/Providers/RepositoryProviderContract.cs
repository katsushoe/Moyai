using System.Text.Json;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

/// <summary>GithubieとBuckettieに共通するRepository Provider Tool契約です。</summary>
public static class RepositoryProviderContract
{
    public static string ToolName(string toolPrefix, RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.ProviderVersion => "get_version",
        _ => $"{toolPrefix}_{OperationName(operation)}",
    };

    public static IReadOnlyDictionary<string, object?> Arguments(RepositoryProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new Dictionary<string, object?> { ["repository"] = request.Project };
        if (request.Operation == RepositoryOperation.ProviderVersion) arguments.Clear();
        if (request.Operation == RepositoryOperation.Commit) arguments["message"] = request.Message;
        if (request.Operation is RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete) arguments["branch"] = request.Branch;
        if (request.Operation == RepositoryOperation.BranchCreate) arguments["source"] = request.BranchSource;
        if (request.Operation is RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush) arguments["tag"] = request.Tag;
        return arguments;
    }

    public static string OperationName(RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.ProviderVersion => "provider_version",
        RepositoryOperation.ProviderCapabilities => "provider_capabilities",
        RepositoryOperation.Status => "repository_status",
        RepositoryOperation.Diff => "repository_diff",
        RepositoryOperation.Commit => "repository_commit",
        RepositoryOperation.Push => "push",
        RepositoryOperation.Pull => "pull",
        RepositoryOperation.BranchList => "branch_list",
        RepositoryOperation.BranchCreate => "branch_create",
        RepositoryOperation.BranchDelete => "branch_delete",
        RepositoryOperation.TagCreate => "tag_create",
        RepositoryOperation.TagDelete => "tag_delete",
        RepositoryOperation.TagPush => "tag_push",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    public static string NormalizeErrorCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "provider_operation_failed";
        try
        {
            using JsonDocument document = JsonDocument.Parse(detail);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("retryable", out JsonElement retryableElement)
                    && retryableElement.ValueKind == JsonValueKind.True) return "provider_retryable_failure";
                if (error.TryGetProperty("code", out JsonElement codeElement)
                    && codeElement.ValueKind == JsonValueKind.String) return NormalizeKnownCode(codeElement.GetString());
            }
        }
        catch (JsonException)
        {
            return NormalizeTextError(detail);
        }
        return NormalizeTextError(detail);
    }

    private static string NormalizeTextError(string detail)
    {
        string value = detail.ToLowerInvariant();
        if (value.Contains("unauthorized", StringComparison.Ordinal) || value.Contains("authentication", StringComparison.Ordinal) || value.Contains("token", StringComparison.Ordinal)) return "provider_authentication_failed";
        if (value.Contains("policy", StringComparison.Ordinal) || value.Contains("protected", StringComparison.Ordinal) || value.Contains("forbidden", StringComparison.Ordinal)) return "provider_policy_rejected";
        if (value.Contains("conflict", StringComparison.Ordinal) || value.Contains("already exists", StringComparison.Ordinal)) return "provider_conflict";
        if (value.Contains("not found", StringComparison.Ordinal)) return "provider_not_found";
        return "provider_operation_failed";
    }

    private static string NormalizeKnownCode(string? code) => code switch
    {
        "provider_unavailable" => "provider_unavailable",
        "unauthorized" or "invalid_service_token" or "service_token_expired" or "service_token_scope_missing" => "provider_authentication_failed",
        "policy_rejected" or "protected_branch" or "forbidden" or "repository_not_allowed" => "provider_policy_rejected",
        "retryable" or "rate_limited" or "temporarily_unavailable" => "provider_retryable_failure",
        "conflict" or "already_exists" => "provider_conflict",
        "not_found" or "repository_not_found" or "branch_not_found" or "tag_not_found" => "provider_not_found",
        _ => "provider_operation_failed",
    };
}
