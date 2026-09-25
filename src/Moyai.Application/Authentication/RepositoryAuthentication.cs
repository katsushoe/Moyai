namespace Moyai.Application.Authentication;

/// <summary>移行期間の明示設定です。期限を過ぎたLegacyは拒否します。</summary>
public sealed record RepositoryAuthentication(string Mode = "assertion", DateTimeOffset? LegacyStartedAt = null, DateTimeOffset? LegacyUntil = null)
{
    public bool UseLegacy(TimeProvider clock)
    {
        if (Mode == "assertion") return false;
        if (Mode != "legacy" || LegacyStartedAt is null || LegacyUntil is null
            || LegacyUntil <= LegacyStartedAt || LegacyUntil - LegacyStartedAt > TimeSpan.FromDays(7)
            || clock.GetUtcNow() < LegacyStartedAt || clock.GetUtcNow() >= LegacyUntil)
            throw new ProviderAuthenticationException("authentication_unavailable");
        return true;
    }
}
