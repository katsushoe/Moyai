using System.Text.Json;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Domain.Events;
using Moyai.Domain.Releases;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.Releases;

/// <summary>ReleaseのWorkItem関連とArtifact Metadataを管理します。</summary>
public sealed class ReleaseContentService(IProjectRepository projects, IWorkItemRepository workItems, IReleaseRepository releases, IReleaseContentRepository content, TimeProvider timeProvider)
{
    public async Task<ReleaseWorkItem> AddItemAsync(AddReleaseItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(command.Project, command.Version, cancellationToken).ConfigureAwait(false);
        WorkItem item = await workItems.GetAsync(project.Id, command.WorkItemKey, false, cancellationToken).ConfigureAwait(false) ?? throw new WorkItemNotFoundException(command.WorkItemKey);
        ReleaseWorkItem relation = ReleaseWorkItem.Create(project.Id, release.Id, item.Id, command.Relation, timeProvider);
        await content.AddWorkItemAsync(relation, Event(project.Id, release.Id, "release_item_added", command.ActorType, command.ActorName, relation), cancellationToken).ConfigureAwait(false);
        return relation;
    }

    public async Task<IReadOnlyList<ReleaseWorkItem>> ListItemsAsync(string projectName, string version, CancellationToken cancellationToken = default)
    {
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(projectName, version, cancellationToken).ConfigureAwait(false);
        return await content.ListWorkItemsAsync(project.Id, release.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveItemAsync(string projectName, string version, Guid relationId, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(projectName, version, cancellationToken).ConfigureAwait(false);
        return await content.RemoveWorkItemAsync(project.Id, release.Id, relationId, Event(project.Id, release.Id, "release_item_removed", actorType, actorName, new { relationId }), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReleaseArtifact> AddArtifactAsync(AddReleaseArtifactCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(command.Project, command.Version, cancellationToken).ConfigureAwait(false);
        ReleaseArtifact artifact = ReleaseArtifact.Create(project.Id, release.Id, command.BuildArtifactId, command.Name, command.ArtifactType, command.Platform, command.Architecture, command.FileName, command.FilePath, command.DownloadUrl, command.FileSize, command.Sha256, command.SignaturePath, command.SignatureUrl, timeProvider);
        await content.AddArtifactAsync(artifact, Event(project.Id, release.Id, "artifact_added", command.ActorType, command.ActorName, artifact), cancellationToken).ConfigureAwait(false);
        return artifact;
    }

    public async Task<IReadOnlyList<ReleaseArtifact>> ListArtifactsAsync(string projectName, string version, CancellationToken cancellationToken = default)
    {
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(projectName, version, cancellationToken).ConfigureAwait(false);
        return await content.ListArtifactsAsync(project.Id, release.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveArtifactAsync(string projectName, string version, Guid artifactId, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        (Moyai.Domain.Projects.Project project, Release release) = await ResolveAsync(projectName, version, cancellationToken).ConfigureAwait(false);
        return await content.RemoveArtifactAsync(project.Id, release.Id, artifactId, Event(project.Id, release.Id, "artifact_removed", actorType, actorName, new { artifactId }), cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Moyai.Domain.Projects.Project Project, Release Release)> ResolveAsync(string projectName, string version, CancellationToken cancellationToken)
    {
        Moyai.Domain.Projects.Project project = await projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
        Release release = await releases.GetAsync(project.Id, version, false, cancellationToken).ConfigureAwait(false) ?? throw new ReleaseNotFoundException(version);
        return (project, release);
    }

    private ProjectEvent Event(Guid projectId, Guid releaseId, string type, string actorType, string actorName, object value) => ProjectEvent.Create(projectId, "release", releaseId, type, actorType, actorName, null, JsonSerializer.Serialize(value), null, timeProvider);
}
