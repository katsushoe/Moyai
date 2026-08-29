using Moyai.Domain.Builds;
using Moyai.Domain.Events;

namespace Moyai.Application.Builds;

/// <summary>BuildとArtifactの永続化境界です。</summary>
public interface IBuildRepository
{
    Task AddAsync(Build build, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task UpdateAsync(Build build, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<Build?> GetAsync(Guid projectId, Guid buildId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Build>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildArtifact>> ListArtifactsAsync(Guid projectId, Guid buildId, CancellationToken cancellationToken = default);
    Task AddArtifactAsync(BuildArtifact artifact, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
}
