using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moyai.Application.Providers;

/// <summary>Provider間で共通化した、作成されたPRの情報です。未返却draftはnullです。</summary>
public sealed record PullRequestInfo(long Id, string? Url, string Title, string? Description, string SourceBranch, string DestinationBranch, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? Draft, string State);

/// <summary>PRの型付きエラーと再送判断に必要な結果状態です。</summary>
public sealed record PullRequestError(string Code, string Outcome, string SuggestedAction, string? ProviderDetail = null);

/// <summary>Provider固有の成功スキーマを公開しない共通PR作成結果です。</summary>
public sealed record PullRequestResult(bool Ok, string Operation, string Project, string? Provider, PullRequestInfo? PullRequest, PullRequestError? Error)
{
    /// <summary>失敗または結果不明を区別します。</summary>
    public static PullRequestResult Failure(string project, string? provider, string code, bool unknown = false, string? detail = null) =>
        new(false, "pull_request_create", project, provider, null, new(code, unknown ? "unknown" : "not_executed",
            unknown ? "Inspect Provider pull requests before another create; do not replay automatically." : "Check input, capability, authorization and Provider route policy.", detail));

    /// <summary>Githubie/Buckettieの成功情報を共通フィールドへ変換します。</summary>
    public static PullRequestResult Parse(string project, string provider, RepositoryProviderResult result)
    {
        if (!result.Ok) return Failure(project, provider, result.ErrorCode ?? "provider_operation_failed", result.ResultUnknown, result.ErrorMessage);
        try
        {
            bool github = provider is "githubie" or "githubbie";
            if (!github && provider != "buckettie") throw new JsonException();
            using JsonDocument document = JsonDocument.Parse(result.Output ?? "null");
            JsonElement data = document.RootElement.GetProperty("data");
            long id = data.GetProperty(github ? "number" : "id").GetInt64();
            if (id <= 0) throw new JsonException();
            var info = new PullRequestInfo(id, OptionalString(data, "url"), RequiredString(data, "title"),
                OptionalString(data, github ? "body" : "description"), RequiredString(data, github ? "source" : "source_branch"),
                RequiredString(data, github ? "destination" : "destination_branch"), OptionalBoolean(data, "draft"), RequiredString(data, "state"));
            return new(true, "pull_request_create", project, github ? "githubie" : "buckettie", info, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return Failure(project, provider, "provider_invalid_response", true);
        }
    }

    private static string RequiredString(JsonElement data, string key) =>
        data.GetProperty(key).GetString() is { Length: > 0 } value ? value : throw new JsonException();
    private static string? OptionalString(JsonElement data, string key) =>
        data.TryGetProperty(key, out JsonElement value) ? value.GetString() : null;
    private static bool? OptionalBoolean(JsonElement data, string key) =>
        !data.TryGetProperty(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null ? null : value.GetBoolean();
}
