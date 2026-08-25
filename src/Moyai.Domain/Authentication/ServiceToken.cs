using System.Security.Cryptography;

namespace Moyai.Domain.Authentication;

/// <summary>Provider向け内部Service Tokenを表します。</summary>
public sealed class ServiceToken
{
    private const int TokenByteLength = 32;
    private readonly HashSet<string> _scopes;

    /// <summary>CSPRNGを使用して256-bitのService Tokenを発行します。</summary>
    public static ServiceToken Issue(string audience, IEnumerable<string> scopes, DateTimeOffset? expiresAt, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(timeProvider);

        HashSet<string> normalizedScopes = scopes
            .Select(static scope => scope?.Trim() ?? string.Empty)
            .Where(static scope => scope.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (normalizedScopes.Count == 0) throw new ArgumentException("At least one scope is required.", nameof(scopes));

        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        if (expiresAt is not null && expiresAt <= issuedAt) throw new ArgumentOutOfRangeException(nameof(expiresAt));

        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenByteLength));
        return new ServiceToken(Guid.NewGuid(), token, audience.ToLowerInvariant(), normalizedScopes, issuedAt, expiresAt);
    }

    private ServiceToken(Guid id, string token, string audience, HashSet<string> scopes, DateTimeOffset issuedAt, DateTimeOffset? expiresAt)
    {
        Id = id;
        Token = token;
        Audience = audience;
        _scopes = scopes;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>永続化されたService Tokenを復元します。</summary>
    public static ServiceToken Restore(Guid id, string token, string audience, IEnumerable<string> scopes, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, DateTimeOffset? lastUsedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Token ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);
        HashSet<string> restoredScopes = scopes.ToHashSet(StringComparer.Ordinal);
        if (restoredScopes.Count == 0) throw new ArgumentException("At least one scope is required.", nameof(scopes));
        var serviceToken = new ServiceToken(id, token, audience, restoredScopes, issuedAt, expiresAt);
        serviceToken.LastUsedAt = lastUsedAt;
        return serviceToken;
    }

    public Guid Id { get; }
    public string Token { get; }
    public string Audience { get; }
    public IReadOnlySet<string> Scopes => _scopes;
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>audience、scope、期限が一致する場合に利用時刻を記録します。</summary>
    public bool Introspect(string audience, string scope, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(timeProvider);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (ExpiresAt is not null && ExpiresAt <= now) return false;
        if (!string.Equals(Audience, audience, StringComparison.Ordinal)) return false;
        if (!_scopes.Contains(scope)) return false;
        LastUsedAt = now;
        return true;
    }
}
