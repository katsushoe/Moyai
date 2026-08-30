using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Lifecycle;

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
            var arguments = new Dictionary<string, object?> { ["repository"] = request.Project, ["project"] = request.Project };
            Add(arguments, "version", request.Version);
            Add(arguments, "artifactPath", request.ArtifactPath);
            Add(arguments, "notes", request.Notes);
            CallToolResult result = await client.CallToolAsync($"{_options.ToolPrefix}_{operation}", arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            string? output = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return result.IsError is true ? new LifecycleResult(false, operation, null, "provider_operation_failed", output) : new LifecycleResult(true, operation, output, null, null);
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
