using System.Net;
using System.Text.Json;

namespace Moyai.Infrastructure.Providers;

/// <summary>1つの検証済み世代にだけ資格情報を送信します。再送は行いません。</summary>
internal sealed class ProviderConnection : IDisposable
{
    private readonly UserHostConnection? _owner;
    private readonly bool _githubie;
    public Uri Endpoint { get; }
    public HttpClient Client { get; }
    public bool ToolDispatched { get; private set; }
    public bool OwnerGateRejected { get; private set; }
    public string? PreDispatchErrorCode { get; private set; }
    private string? _dispatchTool;

    internal void BeginTool(string tool)
    {
        _dispatchTool = tool;
        ToolDispatched = false;
        OwnerGateRejected = false;
        PreDispatchErrorCode = null;
    }

    internal ProviderConnection(McpRepositoryProviderOptions options, IHttpClientFactory factory,
        Func<string, string, UserHostConnection>? resolve = null)
    {
        _githubie = options.UsesGithubieUserHost;
        if (_githubie)
        {
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akatsukisoft", "Githubie");
            _owner = resolve is not null ? resolve(profile, options.ExpectedMcpPath)
                : options.ConnectionResolver is not null ? options.ConnectionResolver(profile, options.ExpectedMcpPath)
                : GithubieUserHostResolver.Resolve(profile, options.RuntimeExecutable!);
        }
        else _owner = options.UsesUserHost ? (resolve ?? options.ConnectionResolver ?? BuckettieUserHostResolver.Resolve)(options.RuntimeDirectory!, options.ExpectedMcpPath) : null;
        if (options.ExpectedGeneration is not null && _owner?.Generation != options.ExpectedGeneration)
        {
            _owner?.Dispose();
            throw new UserHostConnectionException("host_discovery_stale");
        }
        Endpoint = _owner?.Endpoint ?? options.Endpoint!;
        HttpClient inner = factory.CreateClient(options.Name);
        if (options.UsesWindowsOwnerAuthentication && (inner.DefaultRequestHeaders.Contains("Authorization")
            || _githubie && (inner.DefaultRequestHeaders.Contains("X-Githubie-Session") || inner.DefaultRequestHeaders.Contains("X-Githubie-Generation"))))
        {
            inner.Dispose();
            _owner?.Dispose();
            throw new Moyai.Application.Authentication.ProviderAuthenticationException("provider_authentication_mode_mismatch");
        }
        Client = _owner is null ? inner : new HttpClient(new OwnerHandler(this, inner)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string? Redact(string? value) => _owner?.Redact(value) ?? value;
    public void Dispose() { Client.Dispose(); _owner?.Dispose(); }

    private sealed class OwnerHandler(ProviderConnection connection, HttpClient inner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != connection.Endpoint)
                throw new UserHostConnectionException("host_discovery_invalid");
            if (connection._githubie && request.Headers.Contains("Authorization"))
                throw new Moyai.Application.Authentication.ProviderAuthenticationException("provider_authentication_mode_mismatch");
            using var outbound = new HttpRequestMessage(request.Method, request.RequestUri)
            { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var header in request.Headers)
                if (!header.Key.Equals("X-Buckettie-Session", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("X-Githubie-Session", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("X-Githubie-Generation", StringComparison.OrdinalIgnoreCase))
                    outbound.Headers.TryAddWithoutValidation(header.Key, header.Value);
            byte[]? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                outbound.Content = new ByteArrayContent(body);
                foreach (var header in request.Content.Headers) outbound.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                try
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("method", out JsonElement method) && method.GetString() == "tools/call")
                    {
                        string? tool = document.RootElement.TryGetProperty("params", out JsonElement parameters)
                            && parameters.TryGetProperty("name", out JsonElement name) ? name.GetString() : null;
                        if (connection._dispatchTool is null || tool == connection._dispatchTool) connection.ToolDispatched = true;
                    }
                }
                catch (JsonException) { throw new UserHostConnectionException("host_discovery_invalid"); }
            }
            string sessionHeader = connection._githubie ? "X-Githubie-Session" : "X-Buckettie-Session";
            outbound.Headers.Add(sessionHeader, connection._owner!.Credential);
            if (connection._githubie) outbound.Headers.Add("X-Githubie-Generation", connection._owner.Generation);
            try
            {
                HttpResponseMessage response = await inner.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    response.Dispose();
                    throw new UserHostConnectionException("host_discovery_invalid");
                }
                if (connection._githubie && response.StatusCode == HttpStatusCode.Forbidden
                    && response.Headers.TryGetValues("X-Githubie-Error", out IEnumerable<string>? errors))
                {
                    string[] codes = errors.ToArray();
                    bool empty = response.Content.Headers.ContentLength == 0;
                    if (response.Content.Headers.ContentLength is null)
                    {
                        await response.Content.LoadIntoBufferAsync(4096).WaitAsync(cancellationToken).ConfigureAwait(false);
                        empty = (await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).Length == 0;
                    }
                    if (empty && codes.Length == 1 && codes[0] is ("user_host_owner_rejected" or "user_host_auth_mode_mismatch"))
                    { connection.PreDispatchErrorCode = codes[0]; connection.OwnerGateRejected = true; }
                }
                else if (!connection._githubie && response.StatusCode is (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable)
                    && response.Content.Headers.ContentLength is null or >= 0 and <= 4096)
                {
                    await response.Content.LoadIntoBufferAsync(4096).WaitAsync(cancellationToken).ConfigureAwait(false);
                    string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    connection.PreDispatchErrorCode = PreDispatchRejection(payload, response.StatusCode);
                    if (connection.PreDispatchErrorCode is not null) connection.OwnerGateRejected = true;
                }
                return response;
            }
            finally
            {
                outbound.Headers.Remove(sessionHeader);
                outbound.Headers.Remove("X-Githubie-Generation");
                if (body is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(body);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    internal static bool IsOwnerRejection(string payload, HttpStatusCode status)
        => PreDispatchRejection(payload, status) is not null;

    private static string? PreDispatchRejection(string payload, HttpStatusCode status)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            BuckettieUserHostResolver.ValidateProperties(doc.RootElement);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object) return null;
            bool rpc = error.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object;
            if (rpc) error = data;
            if (!error.TryGetProperty("outcome", out JsonElement outcome)
                || outcome.ValueKind != JsonValueKind.String || outcome.GetString() != "not_executed"
                || !error.TryGetProperty("code", out JsonElement code) || code.ValueKind != JsonValueKind.String) return null;
            bool accepted = status == HttpStatusCode.Unauthorized
                ? code.GetString() is "user_host_credential_required" or "user_host_credential_invalid"
                    || rpc && code.GetString() is "owner_authentication_assertion_not_supported" or "provider_capability_missing"
                : status == HttpStatusCode.Forbidden && code.GetString() is "user_host_loopback_required" or "user_host_origin_rejected"
                    || status == HttpStatusCode.ServiceUnavailable && rpc && code.GetString() == "repository_busy";
            return accepted ? code.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or UserHostConnectionException) { return null; }
    }
}
