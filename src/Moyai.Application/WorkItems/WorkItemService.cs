using System.Text.Json;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

/// <summary>WorkItemのApplication操作を提供します。</summary>
public sealed class WorkItemService
{
    private readonly IProjectRepository _projects;
    private readonly IWorkItemRepository _items;
    private readonly TimeProvider _timeProvider;

    public WorkItemService(IProjectRepository projects, IWorkItemRepository items, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _projects = projects;
        _items = items;
        _timeProvider = timeProvider;
    }

    public async Task<WorkItem> CreateAsync(CreateWorkItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Moyai.Domain.Projects.Project project = await GetProjectAsync(command.Project, cancellationToken).ConfigureAwait(false);
        return await _items.AddAsync(
            project.Id,
            command.Type,
            sequence => WorkItem.Create(project.Id, command.Type, sequence, command.Title, command.ActorType, command.ActorName, _timeProvider),
            item => CreateEvent(item, "item_created", command.ActorType, command.ActorName, null, JsonSerializer.Serialize(item)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkItem> GetAsync(string projectName, string key, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        Moyai.Domain.Projects.Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _items.GetAsync(project.Id, key, includeDeleted, cancellationToken).ConfigureAwait(false) ?? throw new WorkItemNotFoundException(key);
    }

    public async Task<IReadOnlyList<WorkItem>> ListAsync(string projectName, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        Moyai.Domain.Projects.Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _items.ListAsync(project.Id, includeDeleted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkItem> UpdateAsync(UpdateWorkItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkItem item = await GetAsync(command.Project, command.Key, cancellationToken: cancellationToken).ConfigureAwait(false);
        string beforeJson = JsonSerializer.Serialize(item);
        item.Update(command.Title, command.Description, command.Priority, command.Severity, command.Owner, command.MetadataJson, _timeProvider);
        ProjectEvent projectEvent = CreateEvent(item, "item_updated", command.ActorType, command.ActorName, beforeJson, JsonSerializer.Serialize(item));
        await _items.UpdateAsync(item, command.ExpectedRevision, projectEvent, cancellationToken).ConfigureAwait(false);
        return item;
    }

    public async Task<WorkItem> SetDeletedAsync(string projectName, string key, long expectedRevision, bool deleted, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        WorkItem item = await GetAsync(projectName, key, includeDeleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        string beforeJson = JsonSerializer.Serialize(item);
        if (deleted) item.Delete(_timeProvider);
        else item.Restore(_timeProvider);
        string eventType = deleted ? "item_deleted" : "item_restored";
        ProjectEvent projectEvent = CreateEvent(item, eventType, actorType, actorName, beforeJson, JsonSerializer.Serialize(item));
        await _items.UpdateAsync(item, expectedRevision, projectEvent, cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>仕様で許可された次状態へWorkItemを遷移します。</summary>
    public async Task<WorkItem> TransitionAsync(TransitionWorkItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkItem item = await GetAsync(command.Project, command.Key, cancellationToken: cancellationToken).ConfigureAwait(false);
        string beforeJson = JsonSerializer.Serialize(item);
        item.TransitionTo(command.NextStatus, _timeProvider);
        ProjectEvent projectEvent = CreateEvent(item, "item_transitioned", command.ActorType, command.ActorName, beforeJson, JsonSerializer.Serialize(item));
        await _items.UpdateAsync(item, command.ExpectedRevision, projectEvent, cancellationToken).ConfigureAwait(false);
        return item;
    }

    private async Task<Moyai.Domain.Projects.Project> GetProjectAsync(string name, CancellationToken cancellationToken) =>
        await _projects.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(name);

    private ProjectEvent CreateEvent(WorkItem item, string eventType, string actorType, string actorName, string? beforeJson, string? afterJson) =>
        ProjectEvent.Create(item.ProjectId, "work_item", item.Id, eventType, actorType, actorName, beforeJson, afterJson, null, _timeProvider);
}
