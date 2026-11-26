using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

/// <summary>検証済みの同一世代で版・能力と業務要求を処理します。署名/Tokenへ退避しません。</summary>
internal sealed class WindowsOwnerSession : IAsyncDisposable
{
    private readonly ProviderConnection _connection;
    private readonly HttpClientTransport _transport;
    private readonly McpClient _client;
    private readonly WindowsOwnerContract? _contract;
    private readonly GithubieOwnerContract? _githubieContract;
    internal bool SupportsRemoteResolution => _githubieContract?.SupportsRemoteResolution ?? _contract?.SupportsRemoteResolution ?? false;
    internal RepositoryProviderResult Version { get; }
    internal RepositoryProviderResult Capabilities { get; }
    internal bool ToolDispatched => _connection.ToolDispatched;
    internal bool RejectedBeforeDispatch => _connection.OwnerGateRejected;
    internal string? PreDispatchErrorCode => _connection.PreDispatchErrorCode;

    private WindowsOwnerSession(ProviderConnection connection, HttpClientTransport transport, McpClient client,
        WindowsOwnerContract? contract, GithubieOwnerContract? githubieContract, RepositoryProviderResult version, RepositoryProviderResult capabilities)
    { _connection = connection; _transport = transport; _client = client; _contract = contract; _githubieContract = githubieContract; Version = version; Capabilities = capabilities; }

    internal static async Task<WindowsOwnerSession> ConnectAsync(McpRepositoryProviderOptions options, IHttpClientFactory factory, string repository, CancellationToken cancellationToken)
    {
        var connection = new ProviderConnection(options, factory);
        connection.BeginTool("__owner_bootstrap__");
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        { Endpoint = connection.Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, connection.Client);
        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            var arguments = new Dictionary<string, object?>();
            RepositoryProviderResult version = RepositoryProviderResponse.Parse(RepositoryOperation.ProviderVersion,
                await client.CallToolAsync("get_version", arguments, cancellationToken: cancellationToken).ConfigureAwait(false));
            if (!version.Ok || version.Output is null) throw new ProviderAuthenticationException(version.ErrorCode ?? "provider_capability_missing");
            if (options.UsesGithubieUserHost) arguments["repository"] = repository;
            RepositoryProviderResult capabilities = RepositoryProviderResponse.Parse(RepositoryOperation.ProviderCapabilities,
                await client.CallToolAsync(options.UsesGithubieUserHost ? "github_provider_capabilities" : "bitbucket_provider_capabilities", arguments, cancellationToken: cancellationToken).ConfigureAwait(false));
            if (!capabilities.Ok || capabilities.Output is null) throw new ProviderAuthenticationException(capabilities.ErrorCode ?? "provider_capability_missing");
            WindowsOwnerContract? contract = null;
            GithubieOwnerContract? githubieContract = null;
            if (options.UsesGithubieUserHost)
            {
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                githubieContract = GithubieOwnerContract.Parse(version.Output, capabilities.Output, tools.Select(tool => tool.Name));
            }
            else contract = WindowsOwnerContract.Parse(version.Output, capabilities.Output);
            return new(connection, transport, client, contract, githubieContract,
                version with { Output = connection.Redact(version.Output) }, capabilities with { Output = connection.Redact(capabilities.Output) });
        }
        catch
        {
            string? refusal = connection.PreDispatchErrorCode;
            if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
            connection.Dispose();
            if (refusal is not null) throw new ProviderAuthenticationException(refusal);
            throw;
        }
    }

    internal async Task<CallToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        RequireTool(tool);
        _connection.BeginTool(tool);
        return await _client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal void RequireTool(string tool)
    {
        if (_githubieContract is not null) _githubieContract.Require(tool);
        else _contract!.Require(tool);
    }

    internal string? Redact(string? value) => _connection.Redact(value);

    public async ValueTask DisposeAsync()
    {
        try { await _client.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ModelContextProtocol.McpException or ObjectDisposedException) { }
        finally
        {
            try { await _transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ModelContextProtocol.McpException or ObjectDisposedException) { }
            finally { _connection.Dispose(); }
        }
    }
}
