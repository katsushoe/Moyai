using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

/// <summary>Repository Providerの通信結果と業務結果を標準応答へ変換します。</summary>
public static class RepositoryProviderResponse
{
    /// <summary>MCP成功だけで業務成功を推定せず、Providerの成否を評価します。</summary>
    public static RepositoryProviderResult Parse(RepositoryOperation operation, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        string? output = result.StructuredContent?.GetRawText() ?? text;
        string name = RepositoryProviderContract.OperationName(operation);
        if (operation is RepositoryOperation.PullRequestPublishCreate or RepositoryOperation.PullRequestPublishStatus) return PublicationResponseParser.Parse(operation, result, output, text);
        if (result.IsError is true) return Failure(name, output);
        if (string.IsNullOrWhiteSpace(output)) return Invalid(name, "Provider returned no JSON result.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            bool? ok = ReadStatus(document.RootElement);
            if (operation is RepositoryOperation.PullRequestMerge or RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete && result.StructuredContent is not null && text is not null)
            {
                using JsonDocument alternate = JsonDocument.Parse(text);
                if (ReadStatus(alternate.RootElement) != ok) return Invalid(name, "Provider result channels disagree.");
                if (operation is RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete
                    && !System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(output), System.Text.Json.Nodes.JsonNode.Parse(text)))
                    return Invalid(name, "Provider mutation result channels disagree.");
            }
            if (ok is false) return Failure(name, output);
            if (result.StructuredContent is not null && text is not null)
            {
                using JsonDocument fallback = JsonDocument.Parse(text);
                bool? textOk = ReadStatus(fallback.RootElement);
                if (textOk is false) return Failure(name, text);
            }
            if (ok is null && operation is not (RepositoryOperation.ProviderVersion or RepositoryOperation.ProviderCapabilities))
                return Invalid(name, "Provider result must contain a boolean ok field.");
            if (document.RootElement.ValueKind is JsonValueKind.Null)
                return Invalid(name, "Provider returned a null result.");
            return new RepositoryProviderResult(true, name, output, null, null);
        }
        catch (JsonException exception)
        {
            return Invalid(name, exception.Message);
        }
    }

    private static bool? ReadStatus(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out JsonElement ok)) return null;
        return ok.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException("Provider ok field must be a boolean."),
        };
    }

    private static RepositoryProviderResult Failure(string operation, string? detail)
    {
        if (operation is "branch_create_local" or "pull_request_create" or "pull_request_get" or "pull_request_merge" or "branch_protection_get" or "branch_protection_update" or "branch_protection_delete" && !HasTypedError(detail)) return Invalid(operation, detail ?? "Provider returned no error detail.");
        return new(false, operation, null, operation switch
        {
            "branch_create_local" => RepositoryProviderContract.NormalizeLocalBranchError(detail),
            "pull_request_create" => RepositoryProviderContract.NormalizeLocalBranchError(detail),
            "pull_request_get" or "pull_request_merge" => RepositoryProviderContract.NormalizeLocalBranchError(detail),
            "branch_protection_get" or "branch_protection_update" or "branch_protection_delete" => RepositoryProviderContract.NormalizeLocalBranchError(detail),
            "branch_switch" => RepositoryProviderContract.NormalizeBranchSwitchError(detail),
            _ => RepositoryProviderContract.NormalizeErrorCode(detail),
        }, detail, operation is "repository_settings_update" or "branch_switch" or "branch_create_local" or "pull_request_create" or "pull_request_merge" && UnknownOutcome(detail)
            || operation is "pull_request_merge" or "branch_protection_update" or "branch_protection_delete" && UnprovenMergeOutcome(detail));
    }

    private static bool HasTypedError(string? detail)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(detail ?? "null");
            JsonElement code = document.RootElement.GetProperty("error").GetProperty("code");
            return code.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(code.GetString());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    private static bool UnknownOutcome(string? detail)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(detail ?? "null");
            return document.RootElement.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("outcome", out JsonElement outcome) && outcome.GetString() == "unknown";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return false; }
    }

    private static RepositoryProviderResult Invalid(string operation, string detail) =>
        new(false, operation, null, "provider_invalid_response", detail, operation is "repository_settings_update" or "branch_switch" or "branch_create_local" or "pull_request_create" or "pull_request_merge" or "branch_protection_update" or "branch_protection_delete");

    private static bool UnprovenMergeOutcome(string? detail)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(detail ?? "null");
            JsonElement error = document.RootElement.GetProperty("error");
            if (error.GetProperty("code").GetString() is "network_error" or "timeout" or "branch_protection_result_unknown") return true;
            return error.GetProperty("outcome").GetString() is not ("not_executed" or "failed" or "pending");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { return true; }
    }
}
