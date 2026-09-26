using System.Text.Json;

namespace Moyai.ProviderAuthentication;

/// <summary>管理者保護されたTrust Bundleを要求ごとに再読込します。</summary>
public sealed class FileAssertionTrustStore : IAssertionTrustStore
{
    private const long MaximumBundleBytes = 1_048_576;
    private readonly string _path;

    /// <summary>絶対パスでTrust Bundleを指定します。</summary>
    public FileAssertionTrustStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Trust Bundle path must be absolute.", nameof(path));
        }

        _path = path;
    }

    /// <inheritdoc />
    public async Task<AssertionTrustKey?> FindAsync(
        string expectedIssuer,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIssuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            if (stream.Length > MaximumBundleBytes)
            {
                throw Unavailable();
            }

            AssertionTrustKey[] keys = await JsonSerializer.DeserializeAsync<AssertionTrustKey[]>(
                stream,
                AssertionJson.Options,
                cancellationToken).ConfigureAwait(false) ?? throw Unavailable();
            if (keys.GroupBy(static key => (key.Issuer, key.Kid)).Any(static group => group.Count() != 1))
            {
                throw Unavailable();
            }

            return keys.SingleOrDefault(key =>
                string.Equals(key.Issuer, expectedIssuer, StringComparison.Ordinal)
                && string.Equals(key.Kid, keyId, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw Unavailable();
        }
    }

    private static ProviderAuthenticationException Unavailable() => new("authentication_unavailable");
}
