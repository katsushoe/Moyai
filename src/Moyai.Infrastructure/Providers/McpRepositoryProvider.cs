using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Providers;

/// <summary>Streamable HTTP MCP経由でGithubbieまたはBuckettieを呼び出します。</summary>
public sealed class McpRepositoryProvider : IRepositoryProvider
{
    private readonly McpRepositoryProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAssertionIssuer? _issuer;
    private readonly AssertionCapability? _capability;
    private readonly IAssertionAudit? _audit;

    public McpRepositoryProvider(McpRepositoryProviderOptions options, IHttpClientFactory httpClientFactory, IAssertionIssuer? issuer = null, AssertionCapability? capability = null, IAssertionAudit? audit = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        if (!options.Endpoint.IsLoopback) throw new ArgumentException("Provider endpoint must use a loopback host.", nameof(options));
        _options = options;
        _httpClientFactory = httpClientFactory;
        _issuer = issuer;
        _capability = capability;
        _audit = audit;
    }

    public string Name => _options.Name;

    public async Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!request.UseAssertion && request.ServiceToken is not null) headers["Authorization"] = $"Bearer {request.ServiceToken}";
            string toolName = RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation);
            AssertionContext? context = null;
            if (request.UseAssertion && !IsBootstrapWithoutAssertion(request.Operation, toolName))
            {
                if (_issuer is null || _capability is null) throw new ProviderAuthenticationException("authentication_unavailable");
                context = new AssertionContext(_capability.ProviderId, request.ProjectId, RepositoryAssertionPolicy.NormalizeRepository(request.RepositoryUrl), toolName,
                    [RepositoryAssertionPolicy.Scope(request.Operation)], request.OperationId ?? throw new ProviderAuthenticationException("auth_project_mismatch"));
                _capability.Require(context);
            }
            if (RequiresIntegrationModeCheck(_options.ToolPrefix, request.Operation)
                && !await IsMoyaiIntegrationModeAsync(cancellationToken).ConfigureAwait(false))
                return new RepositoryProviderResult(false, request.Operation.ContractName(), null, "provider_integration_mode_mismatch",
                    "Buckettie is not running in Moyai integration mode; state-changing operations are not delegated.");
            if (IsStashOperation(request.Operation)
                && !await IsStashOperationSupportedAsync(request, cancellationToken).ConfigureAwait(false))
                return new RepositoryProviderResult(false, request.Operation.ContractName(), null, "provider_capability_missing",
                    "The configured Repository Provider does not advertise support for this stash operation; it was not dispatched.");
            if (request.Operation == RepositoryOperation.CommitSelected
                && !await IsSelectedCommitSupportedAsync(request, cancellationToken).ConfigureAwait(false))
                return new RepositoryProviderResult(false, request.Operation.ContractName(), null, "provider_capability_missing",
                    "The configured Repository Provider does not advertise selected commit support; it was not dispatched.");
            bool supportsRemoteResolution = RepositoryProviderContract.SupportsRemoteArgument(_options.ToolPrefix, request.Operation)
                && !string.IsNullOrWhiteSpace(request.RemoteName)
                && await IsRemoteResolutionSupportedAsync(request, cancellationToken).ConfigureAwait(false);
            var transportOptions = new HttpClientTransportOptions { Endpoint = _options.Endpoint, TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = headers };
            using HttpClient providerClient = _httpClientFactory.CreateClient(Name);
            using AssertionHttpHandler? assertionHandler = context is null ? null : new AssertionHttpHandler(providerClient, _issuer!, context, _audit);
            using HttpClient httpClient = assertionHandler is null ? providerClient : new HttpClient(assertionHandler) { Timeout = Timeout.InfiniteTimeSpan };
            await using var transport = new HttpClientTransport(transportOptions, httpClient);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments(_options.ToolPrefix, request, supportsRemoteResolution);
            CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            RepositoryProviderResult parsed = RepositoryProviderResponse.Parse(request.Operation, result);
            if (assertionHandler is not null)
            {
                parsed = parsed with { Output = assertionHandler.Redact(parsed.Output), ErrorMessage = assertionHandler.Redact(parsed.ErrorMessage) };
                if (_audit is not null) await _audit.WriteAsync(context!, assertionHandler.KeyId, parsed.Ok ? "accepted" : parsed.ErrorCode ?? "provider_operation_failed", cancellationToken).ConfigureAwait(false);
            }
            return parsed;
        }
        catch (ProviderAuthenticationException exception)
        {
            return new RepositoryProviderResult(false, request.Operation.ContractName(), null, exception.Code, exception.Code);
        }
        catch (HttpRequestException exception) when (IsAuthenticationRejection(exception))
        {
            return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(request.Operation), null, "provider_authentication_rejected",
                $"Provider rejected authentication (HTTP {(int)exception.StatusCode!}); the operation was not executed.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(request.Operation), null, "provider_unavailable", request.UseAssertion ? "Provider transport failed; operation outcome may be unknown." : exception.Message);
        }
        catch (ModelContextProtocol.McpException exception)
        {
            return request.UseAssertion ? new RepositoryProviderResult(false, request.Operation.ContractName(), null, "provider_operation_failed", "Provider protocol failure; operation outcome may be unknown.") : Failure(request.Operation, exception.Message);
        }
    }

    /// <summary>ProviderのCapabilityに含まれないversion／capability照会は、Bootstrapとして無Assertionで呼び出します。</summary>
    private bool IsBootstrapWithoutAssertion(RepositoryOperation operation, string toolName) =>
        operation is RepositoryOperation.ProviderVersion or RepositoryOperation.ProviderCapabilities
        && (_capability is null || !_capability.ToolScopes.ContainsKey(toolName));

    /// <summary>Providerが認証を拒否したHTTP応答かを返します。</summary>
    public static bool IsAuthenticationRejection(HttpRequestException exception) =>
        exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    /// <summary>Buckettieへ状態変更操作を委譲する前に連携モードの確認が必要かを返します。</summary>
    public static bool RequiresIntegrationModeCheck(string toolPrefix, RepositoryOperation operation) =>
        string.Equals(toolPrefix, "bitbucket", StringComparison.OrdinalIgnoreCase)
        && RepositoryAssertionPolicy.Scope(operation) != "repository.read";

    /// <summary>Bootstrapのcapabilityを無Assertionで取得し、integration_modeがmoyaiかを確認します。</summary>
    private async Task<bool> IsMoyaiIntegrationModeAsync(CancellationToken cancellationToken)
    {
        var transportOptions = new HttpClientTransportOptions { Endpoint = _options.Endpoint, TransportMode = HttpTransportMode.StreamableHttp };
        using HttpClient httpClient = _httpClientFactory.CreateClient(Name);
        await using var transport = new HttpClientTransport(transportOptions, httpClient);
        await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        CallToolResult result = await client.CallToolAsync(RepositoryProviderContract.ToolName(_options.ToolPrefix, RepositoryOperation.ProviderCapabilities),
            new Dictionary<string, object?>(), cancellationToken: cancellationToken).ConfigureAwait(false);
        RepositoryProviderResult parsed = RepositoryProviderResponse.Parse(RepositoryOperation.ProviderCapabilities, result);
        return parsed.Ok && string.Equals(ReadIntegrationMode(parsed.Output), "moyai", StringComparison.Ordinal);
    }

    private async Task<bool> IsRemoteResolutionSupportedAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        RepositoryProviderResult result = await ExecuteAsync(request with
        {
            Operation = RepositoryOperation.ProviderCapabilities,
            RemoteName = null,
            Message = null,
            Branch = null,
            Tag = null,
            BranchSource = null,
        }, cancellationToken).ConfigureAwait(false);
        return result.Ok && RepositoryProviderContract.SupportsRemoteResolution(result.Output);
    }

    private async Task<bool> IsStashOperationSupportedAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        RepositoryProviderResult result = await ExecuteAsync(request with
        {
            Operation = RepositoryOperation.ProviderCapabilities,
            Message = null,
            Branch = null,
            Tag = null,
            BranchSource = null,
            Paths = null,
            StashId = null,
            OperationId = Guid.NewGuid().ToString("N"),
        }, cancellationToken).ConfigureAwait(false);
        return result.Ok && RepositoryProviderContract.SupportsStashOperation(result.Output, request.Operation);
    }

    private async Task<bool> IsSelectedCommitSupportedAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        RepositoryProviderResult result = await ExecuteAsync(request with
        {
            Operation = RepositoryOperation.ProviderCapabilities,
            Message = null,
            Branch = null,
            Tag = null,
            BranchSource = null,
            Paths = null,
            StashId = null,
            ExpectedHead = null,
            Patch = null,
            OperationId = Guid.NewGuid().ToString("N"),
        }, cancellationToken).ConfigureAwait(false);
        return result.Ok && RepositoryProviderContract.SupportsSelectedCommit(result.Output);
    }

    private static bool IsStashOperation(RepositoryOperation operation) => operation is
        RepositoryOperation.StashPush or RepositoryOperation.StashList or RepositoryOperation.StashApply or RepositoryOperation.StashDrop;

    private static string? ReadIntegrationMode(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("authentication", out JsonElement authentication) && authentication.ValueKind == JsonValueKind.Object
                && authentication.TryGetProperty("integration_mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String
                ? mode.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RepositoryProviderResult Failure(RepositoryOperation operation, string? detail)
    {
        string code = RepositoryProviderContract.NormalizeErrorCode(detail);
        return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(operation), null, code, detail);
    }
}
