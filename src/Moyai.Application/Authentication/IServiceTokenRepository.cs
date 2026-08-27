using Moyai.Domain.Authentication;

namespace Moyai.Application.Authentication;

/// <summary>Service Tokenの永続化境界を定義します。</summary>
public interface IServiceTokenRepository
{
    Task AddAsync(ServiceToken token, CancellationToken cancellationToken = default);
    Task<ServiceToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<ServiceToken?> FindByAudienceAsync(string audience, CancellationToken cancellationToken = default);
    Task UpdateLastUsedAtAsync(Guid id, DateTimeOffset lastUsedAt, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task IssueWithEventAsync(ServiceToken token, string actorType, string actorName, CancellationToken cancellationToken = default);
    Task RotateWithEventAsync(ServiceToken token, string actorType, string actorName, CancellationToken cancellationToken = default);
    Task<bool> RevokeWithEventAsync(string audience, string actorType, string actorName, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredWithEventsAsync(DateTimeOffset now, string actorType, string actorName, CancellationToken cancellationToken = default);
}
