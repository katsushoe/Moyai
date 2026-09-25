namespace Moyai.Application.Authentication;

/// <summary>鍵の明示操作です。秘密情報を入出力しません。</summary>
public interface IAssertionAdministration
{
    Task<AssertionTrustKey> PrepareAsync(CancellationToken cancellationToken = default);
    Task<AssertionTrustKey?> GetAsync(string kid, CancellationToken cancellationToken = default);
    Task ActivateAsync(string kid, int overlapSeconds, bool trustDistributionConfirmed, CancellationToken cancellationToken = default);
    Task RevokeAsync(string kid, CancellationToken cancellationToken = default);
    Task<string> RotateProtectorAsync(CancellationToken cancellationToken = default);
    Task<int> RewrapAsync(CancellationToken cancellationToken = default);
}
