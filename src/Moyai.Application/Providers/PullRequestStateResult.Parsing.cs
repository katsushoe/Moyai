using System.Text.Json;

namespace Moyai.Application.Providers;

public sealed partial record PullRequestStateResult
{
    /// <summary>実行前拒否と結果不明を区別し、自動再送を許可しません。</summary>
    public static PullRequestStateResult Failure(string project, string? provider, RepositoryOperation operation, string code,
        string outcome = "not_executed", string? detail = null, string? correlationId = null) =>
        new(false, operation.ContractName(), project, provider, null,
            new(code, outcome, false, outcome is "unknown" or "pending" ? "check_status" : "check_input_and_policy", correlationId, detail));

    /// <summary>合意済みGithubie schemaを読み取り、要求値を応答証拠に代用しません。</summary>
    public static PullRequestStateResult Parse(string project, string provider, long expectedId, RepositoryProviderResult result)
    {
        RepositoryOperation operation = result.Operation == "pull_request_merge" ? RepositoryOperation.PullRequestMerge : RepositoryOperation.PullRequestGet;
        string canonical = provider is "githubie" or "githubbie" ? "githubie" : provider;
        if (!result.Ok)
        {
            string outcome = result.ResultUnknown ? "unknown" : "not_executed";
            string? correlation = null;
            try
            {
                using JsonDocument errorDocument = JsonDocument.Parse(result.ErrorMessage ?? "null");
                JsonElement error = errorDocument.RootElement.GetProperty("error");
                if (!result.ResultUnknown && error.TryGetProperty("outcome", out JsonElement value)
                    && value.GetString() is "not_executed" or "failed" or "pending" or "unknown") outcome = value.GetString()!;
                if (error.TryGetProperty("correlation_id", out JsonElement id)) correlation = id.GetString();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { }
            return Failure(project, canonical, operation, result.ErrorCode ?? "provider_operation_failed", outcome, result.ErrorMessage, correlation);
        }
        try
        {
            if (canonical != "githubie" || result.ResultUnknown) throw new JsonException();
            using JsonDocument document = JsonDocument.Parse(result.Output ?? "null");
            JsonElement root = document.RootElement;
            if (root.GetProperty("ok").ValueKind != JsonValueKind.True) throw new JsonException();
            JsonElement data = root.GetProperty("data");
            long id = data.GetProperty("number").GetInt64();
            string state = Required(data, "state");
            string source = Required(data, "source_commit_sha");
            string? merged = Optional(data, "merge_commit_sha");
            if (id != expectedId || id is <= 0 or > int.MaxValue || state is not ("open" or "closed" or "merged")
                || !IsSha(source) || merged is not null && !IsSha(merged) || state == "merged" && merged is null) throw new JsonException();
            bool? draft = !data.TryGetProperty("draft", out JsonElement draftValue) || draftValue.ValueKind == JsonValueKind.Null
                ? null : draftValue.GetBoolean();
            var snapshot = new PullRequestStateInfo(id, Optional(data, "url"), state, Required(data, "source"),
                Required(data, "destination"), source, merged, draft);
            return new(true, operation.ContractName(), project, canonical, snapshot, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return Failure(project, canonical, operation, "provider_invalid_response",
                operation == RepositoryOperation.PullRequestMerge ? "unknown" : "not_executed", result.Output);
        }
    }

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);
    private static string Required(JsonElement data, string key) =>
        data.GetProperty(key).GetString() is { Length: > 0 } value ? value : throw new JsonException();
    private static string? Optional(JsonElement data, string key) => data.TryGetProperty(key, out JsonElement value) ? value.GetString() : null;
}
