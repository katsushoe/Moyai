using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

public interface IWorkItemCollaborationRepository
{
    Task AddRelationAsync(WorkItemRelation relation, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<WorkItemRelation?> RemoveRelationAsync(Guid projectId, Guid relationId, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemRelation>> ListRelationsAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
    Task<bool> HasDirectedPathAsync(Guid projectId, Guid fromWorkItemId, Guid toWorkItemId, IReadOnlyCollection<string> relationTypes, CancellationToken cancellationToken = default);
    Task AddCommentAsync(WorkItemComment comment, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemComment>> ListCommentsAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
    Task AddTaskLinkAsync(WorkItemTaskLink link, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<WorkItemTaskLink?> RemoveTaskLinkAsync(Guid projectId, Guid linkId, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemTaskLink>> ListTaskLinksAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
    Task AddCommitLinkAsync(WorkItemCommitLink link, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<WorkItemCommitLink?> RemoveCommitLinkAsync(Guid projectId, Guid linkId, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemCommitLink>> ListCommitLinksAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectEvent>> ListHistoryAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default);
}

public sealed record RelationAddResult(WorkItemRelation Relation, IReadOnlyList<string> Warnings);
