using Moyai.Domain.Events;
using Moyai.Domain.Releases;

namespace Moyai.Application.Releases;

/// <summary>Releaseに含まれるWorkItemとArtifact Metadataの永続化境界です。</summary>
public interface IReleaseContentRepository
{
    Task AddWorkItemAsync(ReleaseWorkItem item, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<bool> RemoveWorkItemAsync(Guid projectId, Guid releaseId, Guid relationId, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReleaseWorkItem>> ListWorkItemsAsync(Guid projectId, Guid releaseId, CancellationToken cancellationToken = default);
    Task AddArtifactAsync(ReleaseArtifact artifact, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<bool> RemoveArtifactAsync(Guid projectId, Guid releaseId, Guid artifactId, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReleaseArtifact>> ListArtifactsAsync(Guid projectId, Guid releaseId, CancellationToken cancellationToken = default);
}
