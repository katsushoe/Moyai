using System.Net.Http.Json;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>mTLSで認証するOS Key Store/HSM/KMS Brokerの共通Adapterです。</summary>
public sealed class BrokerKeyEncryptionProvider(IHttpClientFactory clients, string clientName, Uri endpoint) : IKeyEncryptionKeyProvider
{
    public async Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await CallAsync("wrap", new { dek = Convert.ToBase64String(dek.Span), context = Convert.ToBase64String(context.Span) }, cancellationToken).ConfigureAwait(false);
        return new WrappedDataKey(response.RootElement.GetProperty("wrapped_dek").GetBytesFromBase64(), response.RootElement.GetProperty("key_version").GetString() ?? throw Unavailable());
    }

    public async Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        using JsonDocument response = await CallAsync("unwrap", new { wrapped_dek = key.WrappedDek, key_version = key.KeyVersion, context = Convert.ToBase64String(context.Span) }, cancellationToken).ConfigureAwait(false);
        return response.RootElement.GetProperty("dek").GetBytesFromBase64();
    }

    public async Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await CallAsync("rotate", new { }, cancellationToken).ConfigureAwait(false);
        return response.RootElement.GetProperty("key_version").GetString() ?? throw Unavailable();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await CallAsync("health", new { }, cancellationToken).ConfigureAwait(false);
        return response.RootElement.GetProperty("available").GetBoolean();
    }

    private async Task<JsonDocument> CallAsync<T>(string operation, T body, CancellationToken cancellationToken)
    {
        if (endpoint.Scheme != "https" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw Unavailable();
        try
        {
            using HttpClient client = clients.CreateClient(clientName);
            using HttpResponseMessage response = await client.PostAsJsonAsync(new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/" + operation), body, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (data.Length > 65536) throw Unavailable();
                return JsonDocument.Parse(data);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException)
        { throw Unavailable(); }
    }

    private static ProviderAuthenticationException Unavailable() => new("auth_key_provider_unavailable");
}
