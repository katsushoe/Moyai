namespace Moyai.Application.Authentication;

/// <summary>暗号レコードと鍵保護を結び付ける非機密情報です。</summary>
public sealed record SecretContext(Guid SecretId, string OwnerType, string OwnerId, Guid? ProjectId, string SecretKind);

/// <summary>外部Key Protectorで保護されたData Keyです。</summary>
public sealed record WrappedDataKey(byte[] WrappedDek, string KeyVersion);

/// <summary>OS、HSM、外部KMSの鍵管理Contractです。KEKはこの境界から出ません。</summary>
public interface IKeyEncryptionKeyProvider
{
    Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default);
    Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default);
    Task<string> RotateAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>秘密平文もKEKも含まない永続化Envelopeです。</summary>
public sealed record SecretEnvelope(SecretContext Context, byte[] Ciphertext, byte[] Nonce, byte[] Tag,
    byte[] WrappedDek, string KeyVersion, int AadVersion = 1);

/// <summary>暗号化済みレコードの保存Contractです。</summary>
public interface ISecretEnvelopeStore
{
    Task PutAsync(SecretEnvelope envelope, CancellationToken cancellationToken = default);
    Task<SecretEnvelope?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> RewrapAsync(Guid id, string expectedVersion, WrappedDataKey replacement, CancellationToken cancellationToken = default);
}
