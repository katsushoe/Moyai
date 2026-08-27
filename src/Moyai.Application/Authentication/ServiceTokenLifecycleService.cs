using Moyai.Domain.Authentication;

namespace Moyai.Application.Authentication;

/// <summary>Service Tokenの発行、ローテーション、失効、期限切れ削除を管理します。</summary>
public sealed class ServiceTokenLifecycleService
{
    private readonly IServiceTokenRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ServiceTokenLifecycleService(IServiceTokenRepository repository, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _timeProvider = timeProvider;
    }

    /// <summary>Provider用Service Tokenを新規発行します。</summary>
    public async Task<ServiceToken> IssueAsync(string audience, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAt, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteExpiredWithEventsAsync(_timeProvider.GetUtcNow(), actorType, actorName, cancellationToken).ConfigureAwait(false);
        ServiceToken token = ServiceToken.Issue(audience, scopes, expiresAt, _timeProvider);
        await _repository.IssueWithEventAsync(token, actorType, actorName, cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>旧Tokenを即時削除して新Tokenへ置き換えます。</summary>
    public async Task<ServiceToken> RotateAsync(string audience, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAt, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteExpiredWithEventsAsync(_timeProvider.GetUtcNow(), actorType, actorName, cancellationToken).ConfigureAwait(false);
        ServiceToken token = ServiceToken.Issue(audience, scopes, expiresAt, _timeProvider);
        await _repository.RotateWithEventAsync(token, actorType, actorName, cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>Audienceに対応するTokenを物理削除します。</summary>
    public Task<bool> RevokeAsync(string audience, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        _repository.RevokeWithEventAsync(audience, actorType, actorName, cancellationToken);

    /// <summary>期限切れTokenを物理削除し、監査Eventを記録します。</summary>
    public Task<int> DeleteExpiredAsync(string actorType, string actorName, CancellationToken cancellationToken = default) =>
        _repository.DeleteExpiredWithEventsAsync(_timeProvider.GetUtcNow(), actorType, actorName, cancellationToken);
}
