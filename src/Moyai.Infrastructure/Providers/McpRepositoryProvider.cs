using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

/// <summary>Streamable HTTP MCP経由でGithubbieまたはBuckettieを呼び出します。</summary>
public sealed class McpRepositoryProvider : IRepositoryProvider
{
    private readonly McpRepositoryProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public McpRepositoryProvider(McpRepositoryProviderOptions options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        if (!options.Endpoint.IsLoopback) throw new ArgumentException("Provider endpoint must use a loopback host.", nameof(options));
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => _options.Name;

    public async Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
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
            string toolName = RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation);
            IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments(_options.ToolPrefix, request);
            CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            return RepositoryProviderResponse.Parse(request.Operation, result);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(request.Operation), null, "provider_unavailable", exception.Message);
        }
        catch (ModelContextProtocol.McpException exception)
        {
            return Failure(request.Operation, exception.Message);
        }
    }

    private static RepositoryProviderResult Failure(RepositoryOperation operation, string? detail)
    {
        string code = RepositoryProviderContract.NormalizeErrorCode(detail);
        return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(operation), null, code, detail);
    }
}
