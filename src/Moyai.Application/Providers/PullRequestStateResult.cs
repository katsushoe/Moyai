using System.Text.Json.Serialization;

namespace Moyai.Application.Providers;

/// <summary>Providerが報告したPR状態です。未返却draftを推測せずnullで保持します。</summary>
public sealed record PullRequestStateInfo(
    long Id,
    string? Url,
    string State,
    string SourceBranch,
    string DestinationBranch,
    string SourceCommit,
    string? MergeCommit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? Draft);

/// <summary>PR読取・マージの結果照合に必要なエラー情報を保持します。</summary>
public sealed record PullRequestOperationError(
    string Code,
    string Outcome,
    bool Retryable,
    string SuggestedAction,
    string? CorrelationId = null,
    string? ProviderDetail = null);

/// <summary>PR読取・マージの実測状態と、結果不明時の非再送情報です。</summary>
public sealed partial record PullRequestStateResult(
    bool Ok,
    string Operation,
    string Project,
    string? Provider,
    PullRequestStateInfo? PullRequest,
    PullRequestOperationError? Error);
