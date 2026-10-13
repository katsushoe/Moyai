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

    public static IReadOnlyDictionary<string, object?> Arguments(string toolPrefix, RepositoryProviderRequest request, bool supportsRemoteResolution = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new Dictionary<string, object?> { ["repository"] = request.RepositoryId };
        if (request.Operation == RepositoryOperation.ProviderVersion) arguments.Clear();
        if (supportsRemoteResolution && SupportsRemoteArgument(toolPrefix, request.Operation) && !string.IsNullOrWhiteSpace(request.RemoteName)) arguments["remote"] = request.RemoteName;
        if (request.Operation == RepositoryOperation.Commit) arguments["message"] = request.Message;
        if (request.Operation == RepositoryOperation.CommitSelected)
        {
            arguments["message"] = request.Message;
            arguments["expected_head"] = request.ExpectedHead ?? string.Empty;
            arguments["patch"] = request.Patch;
        }
        if (request.Operation == RepositoryOperation.StashPush)
        {
            arguments["message"] = request.Message;
            arguments["paths"] = request.Paths;
        }
        if (request.Operation is RepositoryOperation.StashApply or RepositoryOperation.StashDrop)
            arguments[string.Equals(toolPrefix, "github", StringComparison.OrdinalIgnoreCase) ? "stash_id" : "stashId"] = request.StashId;
        if (request.Operation is RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete) arguments["branch"] = request.Branch;
        if (request.Operation == RepositoryOperation.BranchCreate) arguments["source"] = request.BranchSource;
        if (request.Operation is RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush) arguments["tag"] = request.Tag;
        if (request.Operation == RepositoryOperation.TagCreate)
        {
            arguments["source"] = request.BranchSource;
            if (string.Equals(toolPrefix, "github", StringComparison.OrdinalIgnoreCase)) arguments["message"] = null;
        }
        if (request.Operation == RepositoryOperation.StashPush && request.Paths is null) arguments.Remove("paths");
        return arguments;
    }

    public static string OperationName(RepositoryOperation operation) => operation.ContractName();

    /// <summary>Providerの共通Capability応答がstash操作を明示対応している場合だけtrueを返します。</summary>
    public static bool SupportsStashOperation(string? output, RepositoryOperation operation)
    {
        (string operationName, string githubProperty) = operation switch
        {
            RepositoryOperation.StashPush => ("repository_stash_push", "repository_stash_push"),
            RepositoryOperation.StashList => ("repository_stash_list", "repository_stash_list"),
            RepositoryOperation.StashApply => ("repository_stash_apply", "repository_stash_apply"),
            RepositoryOperation.StashDrop => ("repository_stash_drop", "repository_stash_drop"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        if (string.IsNullOrWhiteSpace(output)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object) return false;
            if (data.TryGetProperty("operations", out JsonElement operations)
                && operations.ValueKind == JsonValueKind.Object
                && operations.TryGetProperty(operationName, out JsonElement supported)
                && supported.ValueKind is JsonValueKind.True or JsonValueKind.False) return supported.GetBoolean();
            return data.TryGetProperty(githubProperty, out JsonElement githubSupported)
                && githubSupported.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Providerが選択commitを明示対応している場合だけtrueを返します。</summary>
    public static bool SupportsSelectedCommit(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object) return false;
            if (data.TryGetProperty("operations", out JsonElement operations)
                && operations.ValueKind == JsonValueKind.Object
                && operations.TryGetProperty("repository_commit_selected", out JsonElement supported)
                && supported.ValueKind is JsonValueKind.True or JsonValueKind.False) return supported.GetBoolean();
            return data.TryGetProperty("repository_commit_selected", out JsonElement githubSupported)
                && githubSupported.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>remote引数を受け取る既存のProvider操作かを返します。</summary>
    public static bool SupportsRemoteArgument(string toolPrefix, RepositoryOperation operation) =>
        operation == RepositoryOperation.Status
        || operation == RepositoryOperation.CommitSelected
        || (operation == RepositoryOperation.Commit && string.Equals(toolPrefix, "bitbucket", StringComparison.OrdinalIgnoreCase));

    public static bool SupportsRemoteResolution(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("remote_resolution", out JsonElement resolution) && resolution.ValueKind == JsonValueKind.Object
                && resolution.TryGetProperty("version", out JsonElement version) && version.TryGetInt32(out int versionNumber) && versionNumber == 1
                && resolution.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String
                && string.Equals(mode.GetString(), "repository_url", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
        "authentication_unavailable" or "auth_assertion_invalid" or "auth_assertion_expired"
            or "auth_assertion_not_yet_valid" or "auth_audience_mismatch" or "auth_scope_denied"
            or "auth_project_mismatch" or "auth_key_unknown" or "auth_key_revoked" or "auth_replay_detected"
            or "auth_key_provider_unavailable" or "auth_secret_decryption_failed" or "auth_protocol_unsupported"
            or "provider_capability_missing" => code,
        "provider_unavailable" => "provider_unavailable",
        "provider_remote_not_found" or "provider_remote_ambiguous" or "provider_remote_mismatch" => code,
        "unauthorized" or "invalid_service_token" or "service_token_expired" or "service_token_scope_missing" => "provider_authentication_failed",
        "policy_rejected" or "protected_branch" or "forbidden" or "repository_not_allowed" => "provider_policy_rejected",
        "retryable" or "rate_limited" or "temporarily_unavailable" => "provider_retryable_failure",
        "conflict" or "already_exists" or "release_already_exists" => "provider_conflict",
        "not_found" or "repository_not_found" or "branch_not_found" or "tag_not_found" or "release_not_found" => "provider_not_found",
        _ => "provider_operation_failed",
    };
}
