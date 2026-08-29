using Moyai.Domain.Events;
using Moyai.Domain.Releases;

namespace Moyai.Application.Releases;

/// <summary>Releaseと監査Eventの永続化境界です。</summary>
public interface IReleaseRepository
{
    Task AddAsync(Release release, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<Release?> GetAsync(Guid projectId, string version, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Release>> ListAsync(Guid projectId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task UpdateAsync(Release release, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
}
