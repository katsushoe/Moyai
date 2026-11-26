using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moyai.Application.Providers;

/// <summary>要求値はProviderの副作用証拠と分離します。</summary>
public sealed record PublicationRequestContext(
    [property: JsonPropertyName("operation_id")] string OperationId,
    [property: JsonPropertyName("source_branch"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SourceBranch,
    [property: JsonPropertyName("destination_branch"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DestinationBranch,
    [property: JsonPropertyName("expected_source_sha"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExpectedSourceSha);

public sealed record PublicationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("suggested_action")] string SuggestedAction,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);

/// <summary>部分結果と結果不明を、成功と失敗の両方で保持します。</summary>
public sealed record PullRequestPublicationResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("request_context")] PublicationRequestContext RequestContext,
    [property: JsonPropertyName("provider_data_available")] bool ProviderDataAvailable,
    [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Data,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] PublicationError? Error,
    [property: JsonPropertyName("requires_reconciliation")] bool RequiresReconciliation)
{
    public static PullRequestPublicationResult Failure(string project, string? provider, RepositoryOperation operation,
        PublicationRequestContext context, string code, bool unknown = false) =>
        new(false, operation.ContractName(), project, provider, context, false, null,
            new(code, unknown ? "unknown" : "not_executed", false, unknown ? "check_status" : "check_input_and_policy", null), unknown);

    public static PullRequestPublicationResult Parse(string project, string provider, RepositoryOperation operation,
        PublicationRequestContext context, RepositoryProviderResult result)
    {
        try
        {
            string? payload = result.Output ?? result.ErrorMessage;
            using JsonDocument document = JsonDocument.Parse(payload ?? "null");
            JsonElement root = document.RootElement;
            ValidateUnique(root);
            if (root.GetProperty("operation").GetString() != operation.ContractName()) throw new JsonException();
            bool ok = root.GetProperty("ok").GetBoolean();
            if (ok != result.Ok) throw new JsonException();
            if (ok && root.TryGetProperty("error", out JsonElement unexpectedError) && unexpectedError.ValueKind != JsonValueKind.Null)
                throw new JsonException();
            JsonElement? data = null;
            bool reconcile = result.ResultUnknown;
            if (root.TryGetProperty("data", out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                string id = Required(value, "operation_id");
                PullRequestPublicationInput.ValidateOperationId(id);
                if (Guid.Parse(id) != Guid.Parse(context.OperationId)) throw new JsonException();
                bool unavailableStatusData = operation == RepositoryOperation.PullRequestPublishStatus && !ok
                    && Optional(value, "source_branch") == "" && Optional(value, "destination_branch") == ""
                    && Optional(value, "source_sha") == "";
                if (!unavailableStatusData)
                {
                string source = Required(value, "source_branch"), destination = Required(value, "destination_branch");
                string sha = Required(value, "source_sha");
                var check = new PullRequestPublicationInput(source, destination, sha, "validation", id, false);
                check.Validate();
                if (Guid.Parse(id) != Guid.Parse(context.OperationId)
                    || context.SourceBranch is not null && context.SourceBranch != source
                    || context.DestinationBranch is not null && context.DestinationBranch != destination
                    || context.ExpectedSourceSha is not null && !string.Equals(context.ExpectedSourceSha, sha, StringComparison.OrdinalIgnoreCase))
                    throw new JsonException();
                string publication = Required(value, "publication_state"), pr = Required(value, "pull_request_state");
                if (!State(publication) || !State(pr)) throw new JsonException();
                reconcile |= publication == "unknown" || pr == "unknown";
                if (value.GetProperty("requires_reconciliation").GetBoolean() != (publication == "unknown" || pr == "unknown"))
                    throw new JsonException();
                foreach (string key in new[] { "pull_request_head_sha", "observed_remote_sha" })
                    if (value.TryGetProperty(key, out JsonElement hash) && hash.ValueKind != JsonValueKind.Null
                        && !PullRequestPublicationInput.IsSha(hash.GetString())) throw new JsonException();
                string stage = Required(value, "stage");
                if (stage.Length > 64 || stage.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new JsonException();
                if (value.TryGetProperty("observed_at", out JsonElement observed) && observed.ValueKind != JsonValueKind.Null)
                    _ = observed.GetDateTimeOffset();
                long? number = value.TryGetProperty("pull_request_number", out JsonElement n) && n.ValueKind != JsonValueKind.Null ? n.GetInt64() : null;
                if (number is <= 0 or > int.MaxValue) throw new JsonException();
                if (value.TryGetProperty("pull_request_url", out JsonElement url) && url.ValueKind != JsonValueKind.Null
                    && (!Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri) || uri.Scheme != "https"
                        || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0))
                    throw new JsonException();
                if (ok && (publication is not ("created" or "already_present") || pr is not ("created" or "already_present")
                    || number is null || reconcile || Optional(value, "pull_request_url") is null
                    || !string.Equals(Optional(value, "pull_request_head_sha"), sha, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Optional(value, "observed_remote_sha"), sha, StringComparison.OrdinalIgnoreCase))) throw new JsonException();
                string[] keys = ["operation_id", "source_branch", "destination_branch", "source_sha", "publication_state",
                    "pull_request_state", "pull_request_number", "pull_request_url", "pull_request_head_sha", "observed_remote_sha",
                    "observed_at", "stage", "requires_reconciliation"];
                data = JsonSerializer.SerializeToElement(keys.Where(key => value.TryGetProperty(key, out _))
                    .ToDictionary(key => key, key => value.GetProperty(key).Clone(), StringComparer.Ordinal));
                }
            }
            if (ok && data is null) throw new JsonException();
            PublicationError? error = null;
            if (!ok)
            {
                JsonElement failure = root.GetProperty("error");
                string code = Required(failure, "code"), outcome = Required(failure, "outcome");
                if (code.Length > 128 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
                    || outcome is not ("not_executed" or "failed" or "unknown")) throw new JsonException();
                reconcile |= outcome == "unknown";
                error = new(code, reconcile ? "unknown" : outcome, false, reconcile ? "check_status" : "check_input_and_policy",
                    SafeCorrelation(Optional(failure, "correlation_id")));
            }
            if (ok && reconcile) throw new JsonException();
            return new(ok, operation.ContractName(), project, provider, context, data is not null, data, error, reconcile);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException
            or ArgumentException or FormatException or OverflowException)
        {
            bool unknown = result.ResultUnknown || result.Ok || result.Output is not null || result.ErrorCode is "provider_invalid_response";
            return Failure(project, provider, operation, context,
                result.Output is not null || result.Ok ? "provider_invalid_response" : result.ErrorCode ?? "provider_invalid_response", unknown);
        }
    }

    private static string? SafeCorrelation(string? value) =>
        value is { Length: <= 128 } && !value.Any(char.IsControl) ? value : null;
    private static bool State(string value) => value is "not_attempted" or "created" or "already_present" or "failed" or "unknown";
    private static string Required(JsonElement data, string key) =>
        data.GetProperty(key).GetString() is { Length: > 0 } value ? value : throw new JsonException();
    private static string? Optional(JsonElement data, string key) =>
        data.TryGetProperty(key, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static void ValidateUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty item in value.EnumerateObject())
            {
                if (!keys.Add(item.Name)) throw new JsonException();
                ValidateUnique(item.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) ValidateUnique(item);
    }
}

