using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Lifecycle;

using System.Text.Json;

namespace Moyai.Infrastructure.Providers;

/// <summary>Streamable HTTP MCP経由でLifecycle Providerを呼び出します。</summary>
public sealed class McpLifecycleProvider : ILifecycleProvider
{
    private readonly McpRepositoryProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public McpLifecycleProvider(McpRepositoryProviderOptions options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        if (!options.Endpoint.IsLoopback) throw new ArgumentException("Provider endpoint must use a loopback host.", nameof(options));
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => _options.Name;

    public async Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.ServiceToken is not null) headers["Authorization"] = $"Bearer {request.ServiceToken}";
            var transportOptions = new HttpClientTransportOptions { Endpoint = _options.Endpoint, TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = headers };
            using HttpClient httpClient = _httpClientFactory.CreateClient(Name);
            await using var transport = new HttpClientTransport(transportOptions, httpClient);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            string operation = OperationName(request.Action);
            var arguments = new Dictionary<string, object?> { ["repository"] = request.Project };
            if (IsGithubie) arguments["project"] = request.Project;
            Add(arguments, "version", request.Version);
            if (IsGithubie && request.Action == LifecycleAction.ReleaseCreate && request.ArtifactPaths is { Count: > 0 })
            {
                arguments["assets"] = request.ArtifactPaths;
            }
            else
            {
                Add(arguments, IsGithubie ? "artifact_path" : "artifactPath", request.ArtifactPath);
            }
            Add(arguments, "notes", request.Notes);
            string toolName = $"{_options.ToolPrefix}_{operation}";
            if (request.Action == LifecycleAction.ReleaseCreate)
            {
                LifecycleResult? existing = await ReconcileExistingReleaseAsync(client, request, operation, cancellationToken).ConfigureAwait(false);
                if (existing is not null) return existing;
            }
            if (IsGithubie && request.Action == LifecycleAction.ReleasePublish && request.ProviderReleaseId is long releaseId)
            {
                toolName = "github_release_update";
                arguments.Remove("version");
                arguments.Remove("artifact_path");
                arguments.Remove("assets");
                arguments.Remove("notes");
                arguments["release_id"] = releaseId;
                arguments["name"] = null;
                arguments["body"] = null;
                arguments["prerelease"] = null;
                arguments["draft"] = false;
            }
            CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            LifecycleResult parsed = LifecycleProviderResponse.Parse(operation, result);
            if (IsGithubie && request.Action == LifecycleAction.ReleaseCreate && parsed.ErrorCode == "provider_conflict")
            {
                LifecycleResult? existing = await ReconcileExistingReleaseAsync(client, request, operation, cancellationToken).ConfigureAwait(false);
                if (existing is not null) return existing;
            }
            return parsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or ModelContextProtocol.McpException)
        {
            return new LifecycleResult(false, OperationName(request.Action), null, "provider_auth_unavailable", exception.Message);
        }
    }

    private static void Add(Dictionary<string, object?> arguments, string name, string? value)
    {
        if (value is not null) arguments[name] = value;
    }

    private bool IsGithubie => string.Equals(_options.ToolPrefix, "github", StringComparison.OrdinalIgnoreCase);

    private async Task<LifecycleResult?> ReconcileExistingReleaseAsync(McpClient client, LifecycleRequest request, string operation, CancellationToken cancellationToken)
    {
        string getTool = IsGithubie ? "github_release_get" : $"{_options.ToolPrefix}_release_get";
        var getArguments = new Dictionary<string, object?> { ["repository"] = request.Project, ["version"] = request.Version };
        if (IsGithubie) getArguments["project"] = request.Project;
        CallToolResult getResponse = await client.CallToolAsync(getTool, getArguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        LifecycleResult found = LifecycleProviderResponse.Parse(operation, getResponse);
        if (!found.Ok) return found.ErrorCode == "provider_not_found" ? null : found;
        if (string.IsNullOrWhiteSpace(found.Output)) return Invalid(operation, "Provider release lookup returned no data.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(found.Output);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
                return Invalid(operation, "Provider release lookup returned no data object.");
            List<string> differences = IsGithubie
                ? GithubDifferences(data, request)
                : BuckettieDifferences(data, request);
            LifecycleResult? commitResult = await VerifyCommitAsync(client, request, operation, differences, cancellationToken).ConfigureAwait(false);
            if (commitResult is not null) return commitResult;
            if (differences.Count > 0) return Conflict(operation, found.Output, differences);

            bool published;
            bool draftRelease;
            if (IsGithubie)
            {
                bool hasDraft = data.TryGetProperty("draft", out JsonElement draft);
                published = hasDraft && draft.ValueKind == JsonValueKind.False;
                draftRelease = hasDraft && draft.ValueKind == JsonValueKind.True;
            }
            else
            {
                string? state = data.TryGetProperty("state", out JsonElement stateElement) ? stateElement.GetString() : null;
                published = string.Equals(state, "published", StringComparison.OrdinalIgnoreCase);
                draftRelease = string.Equals(state, "draft", StringComparison.OrdinalIgnoreCase);
            }
            if (!published && !draftRelease) return Invalid(operation, "Provider release state must be draft or published.");
            long? releaseId = data.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long value) ? value : null;
            if (IsGithubie && draftRelease && releaseId is null) return Invalid(operation, "Githubie draft release must contain an id.");
            return found with { ResourceId = releaseId, AlreadyCompleted = published };
        }
        catch (JsonException exception)
        {
            return Invalid(operation, exception.Message);
        }
    }

    private async Task<LifecycleResult?> VerifyCommitAsync(McpClient client, LifecycleRequest request, string operation, List<string> differences, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CommitHash)) return null;
        string expectedTag = ExpectedTag(request);
        string toolName = IsGithubie ? "github_tag_get" : "bitbucket_tag_get";
        var arguments = new Dictionary<string, object?> { ["repository"] = request.Project, ["tag"] = expectedTag };
        CallToolResult response = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        LifecycleResult tagResult = LifecycleProviderResponse.Parse(operation, response);
        if (!tagResult.Ok) return tagResult;
        try
        {
            using JsonDocument document = JsonDocument.Parse(tagResult.Output!);
            JsonElement data = document.RootElement.GetProperty("data");
            string property = IsGithubie ? "target_commit_sha" : "target_hash";
            string? actual = data.TryGetProperty(property, out JsonElement commit) ? commit.GetString() : null;
            if (!string.Equals(actual, request.CommitHash, StringComparison.OrdinalIgnoreCase)) differences.Add($"commit expected={request.CommitHash}, actual={actual ?? "missing"}");
            return null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Invalid(operation, exception.Message);
        }
    }

    private static List<string> GithubDifferences(JsonElement data, LifecycleRequest request)
    {
        var differences = new List<string>();
        string expectedTag = ExpectedTag(request);
        string? actualTag = data.TryGetProperty("tag", out JsonElement tag) ? tag.GetString() : null;
        if (!string.Equals(actualTag, expectedTag, StringComparison.OrdinalIgnoreCase)) differences.Add($"tag expected={expectedTag}, actual={actualTag ?? "missing"}");
        string[] expectedAssets = ExpectedAssets(request.ArtifactPaths, request.ArtifactPath);
        string[] actualAssets = data.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array
            ? assets.EnumerateArray().Select(static asset => asset.TryGetProperty("name", out JsonElement name) ? name.GetString() : null).Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name!).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (!expectedAssets.SequenceEqual(actualAssets, StringComparer.OrdinalIgnoreCase)) differences.Add($"artifacts expected=[{string.Join(',', expectedAssets)}], actual=[{string.Join(',', actualAssets)}]");
        return differences;
    }

    private static List<string> BuckettieDifferences(JsonElement data, LifecycleRequest request)
    {
        var differences = new List<string>();
        string? actualVersion = data.TryGetProperty("version", out JsonElement version) ? version.GetString() : null;
        if (!string.Equals(actualVersion, request.Version, StringComparison.Ordinal)) differences.Add($"version expected={request.Version}, actual={actualVersion ?? "missing"}");
        string? expectedArtifact = string.IsNullOrWhiteSpace(request.ArtifactPath) ? null : Path.GetFileName(request.ArtifactPath);
        string? actualArtifact = data.TryGetProperty("artifact_name", out JsonElement artifact) && artifact.ValueKind != JsonValueKind.Null ? artifact.GetString() : null;
        if (!string.Equals(expectedArtifact, actualArtifact, StringComparison.OrdinalIgnoreCase)) differences.Add($"artifact expected={expectedArtifact ?? "none"}, actual={actualArtifact ?? "none"}");
        string? actualNotes = data.TryGetProperty("notes", out JsonElement notes) && notes.ValueKind != JsonValueKind.Null ? notes.GetString() : null;
        if (!string.Equals(request.Notes, actualNotes, StringComparison.Ordinal)) differences.Add("notes differ");
        return differences;
    }

    private static string ExpectedTag(LifecycleRequest request) =>
        string.IsNullOrWhiteSpace(request.TagName) ? $"v{request.Version}" : request.TagName;

    private static string[] ExpectedAssets(IReadOnlyList<string>? paths, string? fallback) =>
        (paths ?? (string.IsNullOrWhiteSpace(fallback) ? [] : [fallback]))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LifecycleResult Conflict(string operation, string output, IReadOnlyCollection<string> differences) =>
        new(false, operation, output, "provider_conflict", $"Existing Provider release differs: {string.Join("; ", differences)}");

    private static LifecycleResult Invalid(string operation, string detail) =>
        new(false, operation, null, "provider_invalid_response", detail);

    private static string OperationName(LifecycleAction action) => action switch
    {
        LifecycleAction.Build => "build",
        LifecycleAction.BuildClean => "build_clean",
        LifecycleAction.ReleaseCreate => "release_create",
        LifecycleAction.ReleasePublish => "release_publish",
        LifecycleAction.ReleaseWithdraw => "release_withdraw",
        LifecycleAction.Deploy => "deploy",
        LifecycleAction.DeployRollback => "deploy_rollback",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
