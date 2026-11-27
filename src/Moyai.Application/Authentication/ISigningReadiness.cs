namespace Moyai.Application.Authentication;

/// <summary>鍵や識別子を列挙せず、署名方式の最小限の状態を示します。</summary>
public sealed record SigningReadiness(string State, bool? ActiveKeyPresent = null, bool? KeyValidNow = null,
    bool? ProtectorAvailable = null, bool SigningAttempted = false);

/// <summary>署名や秘密の復号を行わない、読取専用の診断です。</summary>
public interface ISigningReadiness
{
    Task<SigningReadiness> ReadAsync(CancellationToken cancellationToken = default);
}
