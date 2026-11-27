using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Providers;

/// <summary>Streamable HTTP MCP経由でGithubbieまたはBuckettieを呼び出します。</summary>
public sealed partial class McpRepositoryProvider : IRepositoryProvider
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
        options.Validate();
        _options = options;
        _httpClientFactory = httpClientFactory;
        _issuer = issuer;
        _capability = capability;
        _audit = audit;
    }

    public string Name => _options.Name;
    public bool UsesWindowsOwnerAuthentication => _options.UsesWindowsOwnerAuthentication;

    public async Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsPublication(request.Operation)) return await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Operation == RepositoryOperation.PullRequestMerge)
            return await ExecutePullRequestMergeAsync(request, cancellationToken).ConfigureAwait(false);
        if (IsProtection(request.Operation)) return await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        RepositoryProviderResult result = await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        bool read = _options.UsesWindowsOwnerAuthentication
            ? (_options.UsesGithubieUserHost ? GithubieOwnerContract.IsRead(RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation))
                : WindowsOwnerContract.IsRead(RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation)))
            : RepositoryAssertionPolicy.Scope(request.Operation, "buckettie") == "repository.read";
        if (_options.UsesUserHost && !_options.UsesGithubieUserHost && read && !result.ResultUnknown
            && !cancellationToken.IsCancellationRequested && !result.Ok
            && result.ErrorCode is "provider_unavailable" or "provider_authentication_rejected" or "host_discovery_stale" or "user_host_not_running" or "user_host_credential_invalid" or "user_host_credential_required" or "user_host_owner_rejected")
            result = await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        return request.Operation == RepositoryOperation.ProviderCapabilities ? NormalizeProtectionCapabilities(result) : result;
    }

    private async Task<RepositoryProviderResult> ExecuteOnceAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UseWindowsOwnerAuthentication != _options.UsesWindowsOwnerAuthentication
            || request.UseWindowsOwnerAuthentication && (request.UseAssertion || request.ServiceToken is not null))
            return new(false, request.Operation.ContractName(), null, "provider_authentication_mode_mismatch", "Provider authentication mode does not match the request.");
        bool publication = IsPublication(request.Operation);
        if (publication && ValidatePublication(request) is { } invalidPublication) return invalidPublication;
        bool settings = request.Operation is RepositoryOperation.SettingsGet or RepositoryOperation.SettingsUpdate;
        bool branchSwitch = request.Operation == RepositoryOperation.BranchSwitch;
        bool localBranchCreate = request.Operation == RepositoryOperation.BranchCreateLocal;
        bool prCreate = request.Operation == RepositoryOperation.PullRequestCreate;
        bool prState = request.Operation is RepositoryOperation.PullRequestGet or RepositoryOperation.PullRequestMerge;
        bool protection = IsProtection(request.Operation);
        if (protection && ValidateBranchProtection(request) is { } invalidProtection) return invalidProtection;
        if (prState && ValidatePullRequestState(request) is { } invalidPr) return invalidPr;
        if (prCreate)
        {
            if (_options.ToolPrefix is not ("github" or "bitbucket"))
                return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "PR creation contract is not defined for this Provider.");
            if (!request.UseAssertion && !request.UseWindowsOwnerAuthentication) return new(false, request.Operation.ContractName(), null, "auth_scope_denied", "PR creation requires an authenticated Provider route.");
            if (request.PullRequest is null) return new(false, request.Operation.ContractName(), null, "pull_request_invalid", "PR input is required.");
            try { request.PullRequest.Validate(); }
            catch (ArgumentException) { return new(false, request.Operation.ContractName(), null, "pull_request_invalid", "PR input is invalid."); }
        }
        if (localBranchCreate && !string.Equals(_options.ToolPrefix, "github", StringComparison.OrdinalIgnoreCase))
            return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Local branch creation has no agreed tool contract for this Provider; operation not dispatched.");
        if ((branchSwitch || localBranchCreate) && !request.UseAssertion && !request.UseWindowsOwnerAuthentication)
            return new(false, request.Operation.ContractName(), null, "auth_scope_denied", "Branch switching requires Project-bound assertions.");
        if (settings && ((!request.UseAssertion && !request.UseWindowsOwnerAuthentication) || _options.ToolPrefix is not ("github" or "bitbucket")))
            return new(false, request.Operation.ContractName(), null, "auth_scope_denied", "Settings require an agreed Project-bound Provider route.");
        bool settingsDispatched = false;
        if (_options.UsesWindowsOwnerAuthentication) return await ExecuteOwnerOnceAsync(request, cancellationToken).ConfigureAwait(false);
        ProviderConnection? connection = null;
        bool DynamicOutcomeUnknown() => _options.UsesUserHost && connection is { ToolDispatched: true, OwnerGateRejected: false }
            && (request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation) || RepositoryAssertionPolicy.Scope(request.Operation, "buckettie") != "repository.read");
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
                    RepositoryAssertionPolicy.Scopes(request.Operation, _capability.ProviderId), request.OperationId ?? throw new ProviderAuthenticationException("auth_project_mismatch"));
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
            if (publication)
            {
                RepositoryProviderResult capabilities = await ExecuteOnceAsync(request with { Operation = RepositoryOperation.ProviderCapabilities,
                    Publication = null, PublicationOperationId = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName(), ResultUnknown = false };
                if (!SupportsPublication(capabilities.Output, request.Operation))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit publication capabilities required.");
            }
            if (settings)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities, SettingsChanges = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsSettings(capabilities.Output, request.Operation, _options.ToolPrefix))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Settings capability was not advertised; operation not dispatched.");
            }
            if (branchSwitch)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities, Branch = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsBranchSwitch(capabilities.Output))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Branch switch capability was not advertised; operation not dispatched.");
            }
            if (localBranchCreate)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities, Branch = null, BranchSource = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsLocalBranchCreate(capabilities.Output))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Local branch creation capability was not advertised; operation not dispatched.");
            }
            if (prCreate)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities, PullRequest = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsPullRequestCreate(_options.ToolPrefix, capabilities.Output))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "PR creation capability was not advertised; operation not dispatched.");
            }
            if (prState)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities,
                    PullRequestRead = null, PullRequestMerge = null, OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsPullRequestState(capabilities.Output, request.PullRequestMerge is not null))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit PR state capabilities are required.");
            }
            if (protection)
            {
                RepositoryProviderResult capabilities = await ExecuteAsync(request with { Operation = RepositoryOperation.ProviderCapabilities,
                    OperationId = Guid.NewGuid().ToString("N") }, cancellationToken).ConfigureAwait(false);
                if (!capabilities.Ok) return capabilities with { Operation = request.Operation.ContractName() };
                if (!RepositoryProviderContract.SupportsBranchProtection(capabilities.Output, request.Operation))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit hosting protection capability is required.");
            }
            connection = new ProviderConnection(_options, _httpClientFactory);
            var transportOptions = new HttpClientTransportOptions { Endpoint = connection.Endpoint, TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = headers };
            HttpClient providerClient = connection.Client;
            using AssertionHttpHandler? assertionHandler = context is null ? null : new AssertionHttpHandler(providerClient, _issuer!, context, _audit);
            using HttpClient httpClient = assertionHandler is null ? providerClient : new HttpClient(assertionHandler) { Timeout = Timeout.InfiniteTimeSpan };
            await using var transport = new HttpClientTransport(transportOptions, httpClient);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments(_options.ToolPrefix, request, supportsRemoteResolution);
            settingsDispatched = settings || publication || branchSwitch || localBranchCreate || prCreate || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation);
            CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            RepositoryProviderResult parsed = RepositoryProviderResponse.Parse(request.Operation, result);
            parsed = parsed with { Output = connection.Redact(parsed.Output), ErrorMessage = connection.Redact(parsed.ErrorMessage) };
            if (assertionHandler is not null)
            {
                parsed = parsed with { Output = assertionHandler.Redact(parsed.Output), ErrorMessage = assertionHandler.Redact(parsed.ErrorMessage) };
                if (_audit is not null) await _audit.WriteAsync(context!, assertionHandler.KeyId, parsed.Ok ? "accepted" : parsed.ErrorCode ?? "provider_operation_failed", cancellationToken).ConfigureAwait(false);
            }
            return parsed;
        }
        catch (UserHostConnectionException exception)
        {
            return new(false, request.Operation.ContractName(), null, exception.Code, exception.Code, DynamicOutcomeUnknown());
        }
        catch (OperationCanceledException) when (_options.UsesUserHost || settings || publication || branchSwitch || localBranchCreate || prCreate || prState || protection)
        {
            return new(false, request.Operation.ContractName(), null, "provider_unavailable", "Provider transport cancelled.", DynamicOutcomeUnknown() || settingsDispatched && (request.Operation == RepositoryOperation.SettingsUpdate || branchSwitch || localBranchCreate || prCreate || publication || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation)));
        }
        catch (ProviderAuthenticationException exception)
        {
            return new RepositoryProviderResult(false, request.Operation.ContractName(), null, exception.Code, exception.Code);
        }
        catch (HttpRequestException exception) when (IsAuthenticationRejection(exception))
        {
            return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(request.Operation), null, "provider_authentication_rejected",
                _options.UsesUserHost || publication || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation) ? "Provider authentication rejected; inspect state before retrying." : $"Provider rejected authentication (HTTP {(int)exception.StatusCode!}); the operation was not executed.", DynamicOutcomeUnknown() || settingsDispatched && (publication || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation)));
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new RepositoryProviderResult(false, RepositoryProviderContract.OperationName(request.Operation), null, "provider_unavailable", request.UseAssertion || _options.UsesUserHost ? "Provider transport failed; operation outcome may be unknown." : exception.Message, DynamicOutcomeUnknown() || settingsDispatched && (request.Operation == RepositoryOperation.SettingsUpdate || branchSwitch || localBranchCreate || prCreate || publication || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation)));
        }
        catch (ModelContextProtocol.McpException exception)
        {
            return request.UseAssertion || _options.UsesUserHost ? new RepositoryProviderResult(false, request.Operation.ContractName(), null, "provider_operation_failed", "Provider protocol failure; operation outcome may be unknown.", DynamicOutcomeUnknown() || settingsDispatched && (request.Operation == RepositoryOperation.SettingsUpdate || branchSwitch || localBranchCreate || prCreate || publication || request.Operation == RepositoryOperation.PullRequestMerge || IsProtectionMutation(request.Operation))) : Failure(request.Operation, exception.Message);
        }
        finally { connection?.Dispose(); }
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
        && (operation == RepositoryOperation.SettingsGet || RepositoryAssertionPolicy.Scope(operation, "buckettie") != "repository.read");

    /// <summary>Bootstrapのcapabilityを無Assertionで取得し、integration_modeがmoyaiかを確認します。</summary>
    private async Task<bool> IsMoyaiIntegrationModeAsync(CancellationToken cancellationToken)
    {
        using var connection = new ProviderConnection(_options, _httpClientFactory);
        var transportOptions = new HttpClientTransportOptions { Endpoint = connection.Endpoint, TransportMode = HttpTransportMode.StreamableHttp };
        HttpClient httpClient = connection.Client;
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
