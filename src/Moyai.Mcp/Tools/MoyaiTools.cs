using System.ComponentModel;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.WorkItems;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;

namespace Moyai.Mcp.Tools;

/// <summary>MoyaiのProject State操作を公開します。</summary>
[McpServerToolType]
public sealed class MoyaiTools(ProjectService projects, WorkItemService items, AuthIntrospectionService authentication, ProviderRoutingService routing)
{
    [McpServerTool(Name = "get_version", ReadOnly = true), Description("Returns the running Moyai server version.")]
    public static object GetVersion() => new { name = "Moyai", version = typeof(MoyaiTools).Assembly.GetName().Version?.ToString() ?? "0.0.0.0" };

    [McpServerTool(Name = "project_list", ReadOnly = true), Description("Lists projects registered in Moyai.")]
    public Task<IReadOnlyList<Project>> ProjectList(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        projects.ListAsync(includeArchived, cancellationToken);

    [McpServerTool(Name = "project_get", ReadOnly = true), Description("Gets a registered Moyai project by name.")]
    public Task<Project> ProjectGet(string name, CancellationToken cancellationToken = default) => projects.GetAsync(name, cancellationToken);

    [McpServerTool(Name = "project_create"), Description("Creates a project in Moyai and records an audit event.")]
    public Task<Project> ProjectCreate(string name, string sourcePath, string? installPath, string repositoryUrl, string? repositoryProvider, string buildProvider, string deployMode, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        projects.CreateAsync(new CreateProjectCommand(name, sourcePath, installPath, repositoryUrl, repositoryProvider, buildProvider, deployMode, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "project_update"), Description("Updates a Moyai project using optimistic concurrency and records an audit event.")]
    public Task<Project> ProjectUpdate(string currentName, string name, string? description, string? buildConfigJson, string? gitUserName, string? gitUserEmail, string gitRemoteName, string? gitDefaultBranch, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        projects.UpdateAsync(new UpdateProjectCommand(currentName, name, description, buildConfigJson, gitUserName, gitUserEmail, gitRemoteName, gitDefaultBranch, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "project_set_archived"), Description("Archives or restores a Moyai project using optimistic concurrency.")]
    public Task<Project> ProjectSetArchived(string name, long expectedRevision, bool archived, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        projects.SetArchivedAsync(name, expectedRevision, archived, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "work_item_list", ReadOnly = true), Description("Lists work items for a Moyai project.")]
    public Task<IReadOnlyList<WorkItem>> WorkItemList(string project, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        items.ListAsync(project, includeDeleted, cancellationToken);

    [McpServerTool(Name = "work_item_get", ReadOnly = true), Description("Gets a work item by project and key.")]
    public Task<WorkItem> WorkItemGet(string project, string key, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        items.GetAsync(project, key, includeDeleted, cancellationToken);

    [McpServerTool(Name = "work_item_create"), Description("Creates a work item with an atomic Project+Type key and audit event.")]
    public Task<WorkItem> WorkItemCreate(string project, WorkItemType type, string title, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        items.CreateAsync(new CreateWorkItemCommand(project, type, title, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "work_item_update"), Description("Updates editable work item fields using optimistic concurrency.")]
    public Task<WorkItem> WorkItemUpdate(string project, string key, string title, string? description, WorkItemPriority priority, WorkItemSeverity? severity, string? owner, string? metadataJson, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        items.UpdateAsync(new UpdateWorkItemCommand(project, key, title, description, priority, severity, owner, metadataJson, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "work_item_set_deleted"), Description("Soft deletes or restores a work item using optimistic concurrency.")]
    public Task<WorkItem> WorkItemSetDeleted(string project, string key, long expectedRevision, bool deleted, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        items.SetDeletedAsync(project, key, expectedRevision, deleted, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "work_item_transition"), Description("Transitions a work item according to its type workflow and records an audit event.")]
    public Task<WorkItem> WorkItemTransition(string project, string key, string nextStatus, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        items.TransitionAsync(new TransitionWorkItemCommand(project, key, nextStatus, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "auth_introspect", ReadOnly = true), Description("Validates an internal Moyai service token for a provider audience and scope.")]
    public Task<AuthIntrospectionResult> AuthIntrospect(string token, string audience, string scope, CancellationToken cancellationToken = default) =>
        authentication.IntrospectAsync(token, audience, scope, cancellationToken);

    [McpServerTool(Name = "repository_status", ReadOnly = true), Description("Gets repository status through the Provider configured for a Moyai-managed project.")]
    public Task<RepositoryProviderResult> RepositoryStatus(string project, CancellationToken cancellationToken = default) => routing.ExecuteAsync(project, RepositoryOperation.Status, cancellationToken: cancellationToken);

    [McpServerTool(Name = "repository_diff", ReadOnly = true), Description("Gets repository diff through the Provider configured for a Moyai-managed project.")]
    public Task<RepositoryProviderResult> RepositoryDiff(string project, CancellationToken cancellationToken = default) => routing.ExecuteAsync(project, RepositoryOperation.Diff, cancellationToken: cancellationToken);

    [McpServerTool(Name = "repository_commit"), Description("Commits a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryCommit(string project, string message, CancellationToken cancellationToken = default) => routing.ExecuteAsync(project, RepositoryOperation.Commit, message, cancellationToken);

    [McpServerTool(Name = "repository_push"), Description("Pushes a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryPush(string project, CancellationToken cancellationToken = default) => routing.ExecuteAsync(project, RepositoryOperation.Push, cancellationToken: cancellationToken);

    [McpServerTool(Name = "repository_pull"), Description("Pulls a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryPull(string project, CancellationToken cancellationToken = default) => routing.ExecuteAsync(project, RepositoryOperation.Pull, cancellationToken: cancellationToken);
}
