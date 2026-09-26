using System.Security.Cryptography;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>OS保護ストアだけが実装するKEK格納境界です。</summary>
public interface IProtectedKeyStore
{
    Task StoreAsync(string version, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default);
    Task<byte[]> LoadAsync(string version, CancellationToken cancellationToken = default);
}

/// <summary>Keychain/Secret Serviceに格納したKEKによるAEAD Wrapです。</summary>
public sealed class NativeSecretKeyProtector(IProtectedKeyStore store, string activeVersion) : IKeyEncryptionKeyProvider
{
    private string _activeVersion = activeVersion;
    public async Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        if (dek.Length != 32) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        string version = _activeVersion;
        byte[] kek = await store.LoadAsync(version, cancellationToken).ConfigureAwait(false);
        try
        {
            if (kek.Length != 32) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
            byte[] value = new byte[60];
            RandomNumberGenerator.Fill(value.AsSpan(0, 12));
            using var aes = new AesGcm(kek, 16);
            aes.Encrypt(value.AsSpan(0, 12), dek.Span, value.AsSpan(12, 32), value.AsSpan(44, 16), context.Span);
            return new WrappedDataKey(value, version);
        }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }
    public async Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[] kek = await store.LoadAsync(key.KeyVersion, cancellationToken).ConfigureAwait(false);
        byte[] dek = new byte[32];
        try
        {
            if (key.WrappedDek.Length != 60 || kek.Length != 32) throw new CryptographicException();
            using var aes = new AesGcm(kek, 16);
            aes.Decrypt(key.WrappedDek.AsSpan(0, 12), key.WrappedDek.AsSpan(12, 32), key.WrappedDek.AsSpan(44, 16), dek, context.Span);
            return dek;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new ProviderAuthenticationException("auth_secret_decryption_failed");
        }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }
    public async Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string version = Guid.NewGuid().ToString("N");
        try
        {
            await store.StoreAsync(version, key, cancellationToken).ConfigureAwait(false);
            _activeVersion = version;
            return version;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        byte[] key = await store.LoadAsync(_activeVersion, cancellationToken).ConfigureAwait(false);
        try { return key.Length == 32; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
