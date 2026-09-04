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
            Add(arguments, IsGithubie ? "artifact_path" : "artifactPath", request.ArtifactPath);
            if (IsGithubie && request.ArtifactPaths is { Count: > 0 }) arguments["assets"] = request.ArtifactPaths;
            Add(arguments, "notes", request.Notes);
            string toolName = $"{_options.ToolPrefix}_{operation}";
            if (IsGithubie && request.Action == LifecycleAction.ReleasePublish && request.ProviderReleaseId is long releaseId)
            {
                toolName = "github_release_update";
                arguments.Remove("version");
                arguments.Remove("artifact_path");
                arguments.Remove("assets");
                arguments.Remove("notes");
                arguments["release_id"] = releaseId;
                arguments["draft"] = false;
            }
            CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            LifecycleResult parsed = LifecycleProviderResponse.Parse(operation, result);
            if (IsGithubie && request.Action == LifecycleAction.ReleaseCreate && parsed.ErrorCode == "provider_conflict")
            {
                var listArguments = new Dictionary<string, object?> { ["repository"] = request.Project, ["project"] = request.Project };
                CallToolResult listResult = await client.CallToolAsync("github_release_list", listArguments, cancellationToken: cancellationToken).ConfigureAwait(false);
                LifecycleResult listed = LifecycleProviderResponse.Parse(operation, listResult);
                long? existingId = FindReleaseId(listed.Output, request.Version);
                if (listed.Ok && existingId is not null) return listed with { ResourceId = existingId };
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

    private static long? FindReleaseId(string? output, string? version)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(version)) return null;
        using JsonDocument document = JsonDocument.Parse(output);
        string expectedTag = version.StartsWith('v') ? version : $"v{version}";
        return FindReleaseId(document.RootElement, expectedTag);
    }

    private static long? FindReleaseId(JsonElement element, string expectedTag)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool matches = element.TryGetProperty("tag_name", out JsonElement tag) && string.Equals(tag.GetString(), expectedTag, StringComparison.OrdinalIgnoreCase);
            bool draft = !element.TryGetProperty("draft", out JsonElement draftElement) || draftElement.ValueKind == JsonValueKind.True;
            if (matches && draft && element.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long value)) return value;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                long? nested = FindReleaseId(property.Value, expectedTag);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                long? nested = FindReleaseId(item, expectedTag);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

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
