using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>管理者保護されたTrust Bundleを各検証で再読込します。</summary>
public sealed class FileAssertionTrustStore(string path) : IAssertionTrustStore
{
    public async Task<AssertionTrustKey?> FindAsync(string expectedIssuer, string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            if (stream.Length > 1048576) throw new ProviderAuthenticationException("authentication_unavailable");
            AssertionTrustKey[] keys = await JsonSerializer.DeserializeAsync<AssertionTrustKey[]>(stream, AssertionJson.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new ProviderAuthenticationException("authentication_unavailable");
            if (keys.GroupBy(static k => (k.Issuer, k.Kid)).Any(static group => group.Count() != 1))
                throw new ProviderAuthenticationException("authentication_unavailable");
            return keys.SingleOrDefault(k => k.Issuer == expectedIssuer && k.Kid == keyId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }
}
