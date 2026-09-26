using System.Net.Http.Json;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>mTLSで認証するOS Key Store/HSM/KMS Brokerの共通Adapterです。</summary>
public sealed class BrokerKeyEncryptionProvider(IHttpClientFactory clients, string clientName, Uri endpoint) : IKeyEncryptionKeyProvider
{
    public Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default) =>
        CallAsync("wrap", new { dek = Convert.ToBase64String(dek.Span), context = Convert.ToBase64String(context.Span) },
            static root => new WrappedDataKey(root.GetProperty("wrapped_dek").GetBytesFromBase64(), root.GetProperty("key_version").GetString() ?? throw Unavailable()), cancellationToken);

    public Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return CallAsync("unwrap", new { wrapped_dek = key.WrappedDek, key_version = key.KeyVersion, context = Convert.ToBase64String(context.Span) },
            static root => root.GetProperty("dek").GetBytesFromBase64(), cancellationToken);
    }

    public Task<string> RotateAsync(CancellationToken cancellationToken = default) =>
        CallAsync("rotate", new { }, static root => root.GetProperty("key_version").GetString() ?? throw Unavailable(), cancellationToken);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        CallAsync("health", new { }, static root => root.GetProperty("available").GetBoolean(), cancellationToken);

    /// <summary>応答から必要な値を取り出した後にJSON Documentを破棄し、受信バッファを消去します。</summary>
    private async Task<TResult> CallAsync<TBody, TResult>(string operation, TBody body, Func<JsonElement, TResult> read, CancellationToken cancellationToken)
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
                using JsonDocument document = JsonDocument.Parse(data);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw Unavailable();
                return read(document.RootElement);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { throw Unavailable(); }
    }

    private static ProviderAuthenticationException Unavailable() => new("auth_key_provider_unavailable");
}
