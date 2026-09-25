using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>構成済み鍵基盤だけを明示管理します。</summary>
public sealed class AssertionAdministration(SqliteSigningKeyRing? ring, IKeyEncryptionKeyProvider? protector) : IAssertionAdministration
{
    public Task<AssertionTrustKey> PrepareAsync(CancellationToken cancellationToken = default) => RequiredRing().PrepareAsync(cancellationToken);
    public Task<AssertionTrustKey?> GetAsync(string kid, CancellationToken cancellationToken = default) => RequiredRing().GetTrustAsync(kid, cancellationToken);
    public Task ActivateAsync(string kid, int overlapSeconds, bool trustDistributionConfirmed, CancellationToken cancellationToken = default) => RequiredRing().ActivateAsync(kid, overlapSeconds, trustDistributionConfirmed, cancellationToken);
    public Task RevokeAsync(string kid, CancellationToken cancellationToken = default) => RequiredRing().RevokeAsync(kid, cancellationToken);
    public Task<int> RewrapAsync(CancellationToken cancellationToken = default) => RequiredRing().RewrapAsync(cancellationToken);
    public Task<string> RotateProtectorAsync(CancellationToken cancellationToken = default) =>
        (protector ?? throw new ProviderAuthenticationException("auth_key_provider_unavailable")).RotateAsync(cancellationToken);
    private SqliteSigningKeyRing RequiredRing() => ring ?? throw new ProviderAuthenticationException("authentication_unavailable");
}
