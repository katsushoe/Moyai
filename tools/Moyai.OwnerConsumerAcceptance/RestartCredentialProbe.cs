using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>検証済み新世代に旧資格を一度だけ送り、handler前の拒否を確認します。</summary>
internal sealed class RestartCredentialProbe
{
    private int _attempted;

    internal async Task<RestartProbeResult> CheckAsync(UserHostConnection oldConnection, UserHostConnection newConnection,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        return await CheckAsync(oldConnection, newConnection, client, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<RestartProbeResult> CheckAsync(UserHostConnection oldConnection, UserHostConnection newConnection,
        HttpClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oldConnection);
        ArgumentNullException.ThrowIfNull(newConnection);
        ArgumentNullException.ThrowIfNull(client);
        if (string.IsNullOrEmpty(oldConnection.Credential) || oldConnection.Generation == newConnection.Generation
            || newConnection.Endpoint.Scheme != "http" || !newConnection.Endpoint.IsLoopback
            || newConnection.Endpoint.UserInfo.Length != 0 || newConnection.Endpoint.Query.Length != 0 || newConnection.Endpoint.Fragment.Length != 0
            || client.DefaultRequestHeaders.Contains("Authorization") || client.DefaultRequestHeaders.Contains("X-Buckettie-Session"))
            throw new InvalidOperationException("Invalid isolated restart probe.");
        if (Interlocked.Exchange(ref _attempted, 1) != 0) throw new InvalidOperationException("Restart probe cannot be replayed.");
        using var request = new HttpRequestMessage(HttpMethod.Post, newConnection.Endpoint);
        request.Headers.Add("X-Buckettie-Session", oldConnection.Credential);
        request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"owner-restart-acceptance\",\"version\":\"1\"}}}", Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidOperationException("Unexpected owner gate response.");
            await response.Content.LoadIntoBufferAsync(4096).WaitAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            BuckettieUserHostResolver.ValidateProperties(document.RootElement);
            JsonElement error = document.RootElement.GetProperty("error");
            if (error.GetProperty("code").GetString() != "user_host_credential_invalid"
                || error.GetProperty("outcome").GetString() != "not_executed") throw new InvalidOperationException("Unexpected owner gate result.");
            return new RestartProbeResult(true, 401, "user_host_credential_invalid", "not_executed", 1);
        }
        finally { request.Headers.Remove("X-Buckettie-Session"); }
    }
}

internal sealed record RestartProbeResult(bool Ok, int HttpStatus, string ErrorCode, string Outcome, int RequestCount);
