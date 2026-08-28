using System.Text.Json;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

/// <summary>WorkItemのRelation、Comment、外部Link、履歴を管理します。</summary>
public sealed class WorkItemCollaborationService
{
    private static readonly string[] CycleRelations = ["depends_on", "blocks"];
    private readonly IProjectRepository _projects;
    private readonly IWorkItemRepository _items;
    private readonly IWorkItemCollaborationRepository _collaboration;
    private readonly TimeProvider _timeProvider;

    public WorkItemCollaborationService(IProjectRepository projects, IWorkItemRepository items, IWorkItemCollaborationRepository collaboration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(collaboration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _projects = projects;
        _items = items;
        _collaboration = collaboration;
        _timeProvider = timeProvider;
    }

    public async Task<RelationAddResult> AddRelationAsync(string projectName, string sourceKey, string targetKey, string relationType, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem source) = await ResolveAsync(projectName, sourceKey, cancellationToken).ConfigureAwait(false);
        WorkItem target = await GetItemAsync(projectId, targetKey, cancellationToken).ConfigureAwait(false);
        if (string.Equals(relationType, "relates_to", StringComparison.OrdinalIgnoreCase) && source.Id.CompareTo(target.Id) > 0) (source, target) = (target, source);
        WorkItemRelation relation = WorkItemRelation.Create(projectId, source.Id, target.Id, relationType, _timeProvider);
        var warnings = new List<string>();
        if (CycleRelations.Contains(relation.Relation, StringComparer.Ordinal)
            && await _collaboration.HasDirectedPathAsync(projectId, target.Id, source.Id, CycleRelations, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add("relation_cycle_detected");
        }
        await _collaboration.AddRelationAsync(relation, Event(projectId, "work_item_relation", relation.Id, "relation_added", actorType, actorName, null, JsonSerializer.Serialize(relation)), cancellationToken).ConfigureAwait(false);
        return new RelationAddResult(relation, warnings);
    }

    public async Task<bool> RemoveRelationAsync(string projectName, Guid relationId, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Guid projectId = await ResolveProjectIdAsync(projectName, cancellationToken).ConfigureAwait(false);
        ProjectEvent projectEvent = Event(projectId, "work_item_relation", relationId, "relation_removed", actorType, actorName, null, null);
        return await _collaboration.RemoveRelationAsync(projectId, relationId, projectEvent, cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<IReadOnlyList<WorkItemRelation>> ListRelationsAsync(string projectName, string key, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        return await _collaboration.ListRelationsAsync(projectId, item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkItemComment> AddCommentAsync(string projectName, string key, string body, string authorType, string authorName, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        WorkItemComment comment = WorkItemComment.Create(projectId, item.Id, body, authorType, authorName, _timeProvider);
        await _collaboration.AddCommentAsync(comment, Event(projectId, "work_item_comment", comment.Id, "comment_added", authorType, authorName, null, JsonSerializer.Serialize(comment)), cancellationToken).ConfigureAwait(false);
        return comment;
    }

    public async Task<IReadOnlyList<WorkItemComment>> ListCommentsAsync(string projectName, string key, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        return await _collaboration.ListCommentsAsync(projectId, item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkItemTaskLink> AddTaskLinkAsync(string projectName, string key, string taskSystem, string taskId, string relation, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        WorkItemTaskLink link = WorkItemTaskLink.Create(projectId, item.Id, taskSystem, taskId, relation, _timeProvider);
        await _collaboration.AddTaskLinkAsync(link, Event(projectId, "work_item_task_link", link.Id, "task_link_added", actorType, actorName, null, JsonSerializer.Serialize(link)), cancellationToken).ConfigureAwait(false);
        return link;
    }

    public async Task<bool> RemoveTaskLinkAsync(string projectName, Guid linkId, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Guid projectId = await ResolveProjectIdAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _collaboration.RemoveTaskLinkAsync(projectId, linkId, Event(projectId, "work_item_task_link", linkId, "task_link_removed", actorType, actorName, null, null), cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<IReadOnlyList<WorkItemTaskLink>> ListTaskLinksAsync(string projectName, string key, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        return await _collaboration.ListTaskLinksAsync(projectId, item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkItemCommitLink> AddCommitLinkAsync(string projectName, string key, string commitHash, string relation, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        WorkItemCommitLink link = WorkItemCommitLink.Create(projectId, item.Id, commitHash, relation, _timeProvider);
        await _collaboration.AddCommitLinkAsync(link, Event(projectId, "work_item_commit", link.Id, "commit_link_added", actorType, actorName, null, JsonSerializer.Serialize(link)), cancellationToken).ConfigureAwait(false);
        return link;
    }

    public async Task<bool> RemoveCommitLinkAsync(string projectName, Guid linkId, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Guid projectId = await ResolveProjectIdAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _collaboration.RemoveCommitLinkAsync(projectId, linkId, Event(projectId, "work_item_commit", linkId, "commit_link_removed", actorType, actorName, null, null), cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<IReadOnlyList<WorkItemCommitLink>> ListCommitLinksAsync(string projectName, string key, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        return await _collaboration.ListCommitLinksAsync(projectId, item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectEvent>> ListHistoryAsync(string projectName, string key, CancellationToken cancellationToken = default)
    {
        (Guid projectId, WorkItem item) = await ResolveAsync(projectName, key, cancellationToken).ConfigureAwait(false);
        return await _collaboration.ListHistoryAsync(projectId, item.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid ProjectId, WorkItem Item)> ResolveAsync(string projectName, string key, CancellationToken cancellationToken)
    {
        Guid projectId = await ResolveProjectIdAsync(projectName, cancellationToken).ConfigureAwait(false);
        return (projectId, await GetItemAsync(projectId, key, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Guid> ResolveProjectIdAsync(string projectName, CancellationToken cancellationToken) =>
        (await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(projectName)).Id;

    private async Task<WorkItem> GetItemAsync(Guid projectId, string key, CancellationToken cancellationToken) =>
        await _items.GetAsync(projectId, key, false, cancellationToken).ConfigureAwait(false) ?? throw new WorkItemNotFoundException(key);

    private ProjectEvent Event(Guid projectId, string entityType, Guid entityId, string eventType, string actorType, string actorName, string? beforeJson, string? afterJson) =>
        ProjectEvent.Create(projectId, entityType, entityId, eventType, actorType, actorName, beforeJson, afterJson, null, _timeProvider);
}
