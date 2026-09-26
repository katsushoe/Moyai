using System.Security.Cryptography;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>レコードごとにDEKを生成するAES-256-GCM Envelopeです。</summary>
public sealed class SecretEnvelopeCryptor(IKeyEncryptionKeyProvider keys)
{
    public async Task<SecretEnvelope> EncryptAsync(SecretContext context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
    {
        byte[] aad = Aad(context);
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(dek, 16);
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, aad);
            WrappedDataKey wrapped = await keys.WrapAsync(dek, aad, cancellationToken).ConfigureAwait(false);
            return new SecretEnvelope(context, ciphertext, nonce, tag, wrapped.WrappedDek, wrapped.KeyVersion);
        }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    public async Task<byte[]> DecryptAsync(SecretEnvelope envelope, SecretContext expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Context != expected || envelope.AadVersion != 1) throw new ProviderAuthenticationException("auth_secret_decryption_failed");
        byte[] aad = Aad(expected);
        byte[] dek = await keys.UnwrapAsync(new WrappedDataKey(envelope.WrappedDek, envelope.KeyVersion), aad, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            if (dek.Length != 32) throw new CryptographicException();
            using var aes = new AesGcm(dek, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, aad);
            return plaintext;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new ProviderAuthenticationException("auth_secret_decryption_failed");
        }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    public async Task<WrappedDataKey> RewrapAsync(SecretEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        // Verify the complete envelope before replacing its wrapped key.
        byte[] plaintext = await DecryptAsync(envelope, envelope.Context, cancellationToken).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(plaintext);
        byte[] aad = Aad(envelope.Context);
        byte[] dek = await keys.UnwrapAsync(new WrappedDataKey(envelope.WrappedDek, envelope.KeyVersion), aad, cancellationToken).ConfigureAwait(false);
        try { return await keys.WrapAsync(dek, aad, cancellationToken).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    public static byte[] Aad(SecretContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SecretId == Guid.Empty || context.OwnerType is not ("moyai" or "provider")
            || string.IsNullOrWhiteSpace(context.OwnerId) || string.IsNullOrWhiteSpace(context.SecretKind))
            throw new ProviderAuthenticationException("auth_secret_decryption_failed");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            secret_id = context.SecretId.ToString("D"),
            owner_type = context.OwnerType,
            owner_id = context.OwnerId,
            project_id = context.ProjectId?.ToString("D"),
            secret_kind = context.SecretKind,
        });
    }
}
