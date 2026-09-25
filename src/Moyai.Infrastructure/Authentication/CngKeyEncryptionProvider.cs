using System.Runtime.Versioning;
using System.Security.Cryptography;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>Windows CNGの非Export鍵を使い、DEKとAAD HashをOAEPで保護します。</summary>
[SupportedOSPlatform("windows")]
public sealed class CngKeyEncryptionProvider(string keyNamespace, string activeVersion) : IKeyEncryptionKeyProvider
{
    private string _activeVersion = activeVersion;

    public Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dek.Length != 32) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        byte[] data = new byte[64];
        dek.Span.CopyTo(data);
        SHA256.HashData(context.Span).CopyTo(data, 32);
        try
        {
            using CngKey key = CngKey.Open(KeyName(_activeVersion));
            using var rsa = new RSACng(key);
            return Task.FromResult(new WrappedDataKey(rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256), _activeVersion));
        }
        catch (CryptographicException) { throw new ProviderAuthenticationException("auth_key_provider_unavailable"); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using CngKey cng = CngKey.Open(KeyName(key.KeyVersion));
            using var rsa = new RSACng(cng);
            byte[] data = rsa.Decrypt(key.WrappedDek, RSAEncryptionPadding.OaepSHA256);
            try
            {
                if (data.Length != 64 || !CryptographicOperations.FixedTimeEquals(data.AsSpan(32), SHA256.HashData(context.Span)))
                    throw new ProviderAuthenticationException("auth_secret_decryption_failed");
                return Task.FromResult(data.AsSpan(0, 32).ToArray());
            }
            finally { CryptographicOperations.ZeroMemory(data); }
        }
        catch (CryptographicException) { throw new ProviderAuthenticationException("auth_secret_decryption_failed"); }
    }

    public Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string version = Guid.NewGuid().ToString("N");
        try
        {
            var parameters = new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Decryption };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None));
            using CngKey key = CngKey.Create(CngAlgorithm.Rsa, KeyName(version), parameters);
            _activeVersion = version;
            return Task.FromResult(version);
        }
        catch (CryptographicException) { throw new ProviderAuthenticationException("auth_key_provider_unavailable"); }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(_activeVersion) && CngKey.Exists(KeyName(_activeVersion)));
    }

    private string KeyName(string version)
    {
        if (string.IsNullOrWhiteSpace(keyNamespace) || string.IsNullOrWhiteSpace(version)
            || version.Any(static c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        return keyNamespace + "." + version;
    }
}
