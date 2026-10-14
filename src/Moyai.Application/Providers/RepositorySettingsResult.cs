using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moyai.Application.Providers;

/// <summary>Provider登録値だけを返す設定結果です。</summary>
public sealed record RepositorySettings(
    [property: JsonPropertyName("direct_push_branches")] string[] DirectPushBranches,
    [property: JsonPropertyName("pull_branches")] string[] PullBranches,
    [property: JsonPropertyName("protected_branches")] string[] ProtectedBranches,
    [property: JsonPropertyName("tag_target_branch")] string TagTargetBranch,
    [property: JsonPropertyName("tag_pattern")] string TagPattern,
    [property: JsonPropertyName("require_clean_working_tree")] bool RequireCleanWorkingTree,
    [property: JsonPropertyName("commit_author_name")] string? CommitAuthorName,
    [property: JsonPropertyName("commit_author_email")] string? CommitAuthorEmail);

/// <summary>秘密を含むProvider生応答を公開しない共通エラーです。</summary>
public sealed record RepositorySettingsError(string Code, string Outcome, string SuggestedAction, string? CorrelationId = null);

/// <summary>読取・更新の成否、承認と結果不明を区別します。</summary>
public sealed record RepositorySettingsResult(bool Ok, string Operation, string Project, RepositorySettings? Settings,
    string[] ChangedFields, bool Approved, RepositorySettingsError? Error)
{
    /// <summary>成功結果のProvider識別子です。未解決の失敗ではnullです。</summary>
    public string? Provider => Ok ? "githubie" : null;
    /// <summary>失敗または結果不明を型付きで返します。</summary>
    public static RepositorySettingsResult Failure(string project, RepositoryOperation operation, string code, bool unknown = false) =>
        new(false, operation.ContractName(), project, null, [], false,
            new RepositorySettingsError(code, unknown ? "unknown" : "not_executed", unknown ? "Read current settings before submitting another update." : "Check capability, authorization and input; do not bypass the Provider."));

    /// <summary>合意した出力項目だけを実パーサーで読み取ります。</summary>
    public static RepositorySettingsResult Parse(string project, RepositoryOperation operation, RepositoryProviderResult result)
    {
        if (!result.Ok)
        {
            var failure = Failure(project, operation, result.ErrorCode ?? "provider_operation_failed", result.ResultUnknown);
            return failure with { Error = failure.Error! with { CorrelationId = ReadCorrelation(result.ErrorMessage) } };
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Output ?? "null");
            JsonElement data = document.RootElement.GetProperty("data");
            JsonElement settings = data.GetProperty("settings");
            var parsed = new RepositorySettings(ReadArray(settings, "direct_push_branches"), ReadArray(settings, "pull_branches"), ReadArray(settings, "protected_branches"),
                settings.GetProperty("tag_target_branch").GetString() ?? throw new JsonException(),
                settings.GetProperty("tag_pattern").GetString() ?? throw new JsonException(), settings.GetProperty("require_clean_working_tree").GetBoolean(),
                settings.GetProperty("commit_author_name").GetString(), settings.GetProperty("commit_author_email").GetString());
            bool update = operation == RepositoryOperation.SettingsUpdate;
            if (update && data.GetProperty("approved").ValueKind != JsonValueKind.True)
                return Failure(project, operation, "provider_invalid_response", true);
            string[] changed = update ? ReadArray(data, "changed_fields") : [];
            if (changed.Any(static name => name is not ("direct_push_branches" or "pull_branches" or "protected_branches" or "tag_target_branch" or "tag_pattern" or "require_clean_working_tree" or "commit_author_name" or "commit_author_email")))
                throw new JsonException();
            return new(true, operation.ContractName(), project, parsed, changed, update, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Failure(project, operation, "provider_invalid_response", operation == RepositoryOperation.SettingsUpdate);
        }
    }

    private static string[] ReadArray(JsonElement element, string name) => element.GetProperty(name).EnumerateArray().Select(static value => value.GetString() ?? throw new JsonException()).ToArray();

    private static string? ReadCorrelation(string? detail)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(detail ?? "null");
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("error", out JsonElement error) || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("correlation_id", out JsonElement id) || id.ValueKind != JsonValueKind.String) return null;
            string? value = id.GetString();
            return Guid.TryParse(value, out _) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}
