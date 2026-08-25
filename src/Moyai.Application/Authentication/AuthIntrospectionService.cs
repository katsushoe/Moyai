namespace Moyai.Application.Authentication;

/// <summary>ProviderからのService Token検証要求を処理します。</summary>
public sealed class AuthIntrospectionService
{
    private readonly IServiceTokenRepository _repository;
    private readonly TimeProvider _timeProvider;

    public AuthIntrospectionService(IServiceTokenRepository repository, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<AuthIntrospectionResult> IntrospectAsync(string tokenValue, string audience, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        Domain.Authentication.ServiceToken? token = await _repository.FindByTokenAsync(tokenValue, cancellationToken).ConfigureAwait(false);
        if (token is null) return new AuthIntrospectionResult(false, "invalid_service_token");
        if (token.ExpiresAt is not null && token.ExpiresAt <= _timeProvider.GetUtcNow()) return new AuthIntrospectionResult(false, "service_token_expired");
        if (!string.Equals(token.Audience, audience, StringComparison.Ordinal)) return new AuthIntrospectionResult(false, "service_token_audience_mismatch");
        if (!token.Scopes.Contains(scope)) return new AuthIntrospectionResult(false, "service_token_scope_missing");

        bool valid = token.Introspect(audience, scope, _timeProvider);
        if (!valid || token.LastUsedAt is null) return new AuthIntrospectionResult(false, "invalid_service_token");
        await _repository.UpdateLastUsedAtAsync(token.Id, token.LastUsedAt.Value, cancellationToken).ConfigureAwait(false);
        return AuthIntrospectionResult.Success;
    }

    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default) =>
        _repository.DeleteExpiredAsync(_timeProvider.GetUtcNow(), cancellationToken);
}
