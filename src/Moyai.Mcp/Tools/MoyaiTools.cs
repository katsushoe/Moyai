using System.ComponentModel;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;
using Moyai.Application.Builds;
using Moyai.Application.Deployments;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.Releases;
using Moyai.Application.WorkItems;
using Moyai.Domain.Builds;
using Moyai.Domain.Deployments;
using Moyai.Domain.Projects;
using Moyai.Domain.Releases;
using Moyai.Domain.WorkItems;

namespace Moyai.Mcp.Tools;

/// <summary>MoyaiのProject State操作を公開します。</summary>
[McpServerToolType]
public sealed class MoyaiTools(ProjectService projects, ProjectQueryService queries, WorkItemService items, WorkItemCollaborationService collaboration, ReleaseService releases, ReleaseContentService releaseContent, ReleaseOrchestrationService releaseOrchestration, BuildService builds, DeploymentService deployments, AuthIntrospectionService authentication, ProviderRoutingService routing, ServiceTokenLifecycleService tokens)
{
    private static async Task<T> WithConfigurationErrors<T>(Func<Task<T>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ProjectConfigurationException exception) { throw new ModelContextProtocol.McpException(exception.Message); }
    }

    [McpServerTool(Name = "token_issue"), Description("Issues a Provider service token. Returns the secret once; do not log it.")]
    public Task<Moyai.Domain.Authentication.ServiceToken> TokenIssue(string audience, string[] scopes, string actorType, string actorName, DateTimeOffset? expiresAt = null, CancellationToken cancellationToken = default) => tokens.IssueAsync(audience, scopes, expiresAt, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "token_rotate", Destructive = true), Description("Replaces a Provider service token. Returns the new secret once; do not log it.")]
    public Task<Moyai.Domain.Authentication.ServiceToken> TokenRotate(string audience, string[] scopes, string actorType, string actorName, DateTimeOffset? expiresAt = null, CancellationToken cancellationToken = default) => tokens.RotateAsync(audience, scopes, expiresAt, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "token_revoke", Destructive = true), Description("Revokes a Provider service token and records an audit event.")]
    public Task<bool> TokenRevoke(string audience, string actorType, string actorName, CancellationToken cancellationToken = default) => tokens.RevokeAsync(audience, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "token_cleanup", Destructive = true), Description("Removes expired Provider service tokens.")]
    public Task<int> TokenCleanup(string actorType, string actorName, CancellationToken cancellationToken = default) => tokens.DeleteExpiredAsync(actorType, actorName, cancellationToken);

    [McpServerTool(Name = "get_version", ReadOnly = true), Description("Returns the running Moyai server version.")]
    public static object GetVersion() => new { name = "Moyai", version = typeof(MoyaiTools).Assembly.GetName().Version?.ToString() ?? "0.0.0.0" };

    [McpServerTool(Name = "project_list", ReadOnly = true), Description("Lists projects registered in Moyai.")]
    public Task<IReadOnlyList<Project>> ProjectList(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        projects.ListAsync(includeArchived, cancellationToken);

    [McpServerTool(Name = "list_projects", ReadOnly = true), Description("Lists registered project name candidates. Call this before any tool that accepts a project name.")]
    public Task<IReadOnlyList<Project>> ListProjects(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        projects.ListAsync(includeArchived, cancellationToken);

    [McpServerTool(Name = "project_get", ReadOnly = true), Description("Gets a registered Moyai project by name.")]
    public Task<Project> ProjectGet(string name, CancellationToken cancellationToken = default) => projects.GetAsync(name, cancellationToken);

    [McpServerTool(Name = "project_create"), Description("Creates a project in Moyai and records an audit event.")]
    public Task<Project> ProjectCreate(string name, string sourcePath = "", string? installPath = null, string repositoryUrl = "", string? repositoryProvider = null, string buildProvider = "", string deployMode = "", string actorType = "client", string actorName = "unspecified", CancellationToken cancellationToken = default) =>
        projects.CreateAsync(new CreateProjectCommand(name, sourcePath, installPath, repositoryUrl, repositoryProvider, buildProvider, deployMode, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "project_ensure", Idempotent = true), Description("Returns an existing Project or creates it using only its name. Preserves existing settings and archive state. Call before operations when automatic registration is needed.")]
    public Task<Project> ProjectEnsure(string name, string actorType = "client", string actorName = "unspecified", CancellationToken cancellationToken = default) =>
        projects.EnsureAsync(name, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "project_configure"), Description("Associates execution settings with an existing Project using optimistic concurrency. Omitted settings are preserved.")]
    public Task<Project> ProjectConfigure(string name, long expectedRevision, string? sourcePath = null, string? installPath = null, string? repositoryUrl = null, string? repositoryProvider = null, string? buildProvider = null, string? deployMode = null, string actorType = "client", string actorName = "unspecified", CancellationToken cancellationToken = default) =>
        projects.ConfigureAsync(name, expectedRevision, sourcePath, installPath, repositoryUrl, repositoryProvider, buildProvider, deployMode, actorType, actorName, cancellationToken);

    /// <summary>名前だけを変更する操作をCLIとMCPへ公開します。</summary>
    [McpServerTool(Name = "project_rename"), Description("Renames a Project while preserving its ID, settings, archive state, and related data. Requires the current revision and records a project_renamed audit event.")]
    public Task<Project> ProjectRename(string currentName, string name, long expectedRevision, string actorType = "client", string actorName = "unspecified", CancellationToken cancellationToken = default) =>
        projects.RenameAsync(currentName, name, expectedRevision, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "project_update"), Description("Updates a Moyai project using optimistic concurrency and records an audit event.")]
    public Task<Project> ProjectUpdate(string currentName, string name, string? repositoryUrl, string? repositoryProvider, string? description, string? buildConfigJson, string? gitUserName, string? gitUserEmail, string gitRemoteName, string? gitDefaultBranch, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        projects.UpdateAsync(new UpdateProjectCommand(currentName, name, repositoryUrl, repositoryProvider, description, buildConfigJson, gitUserName, gitUserEmail, gitRemoteName, gitDefaultBranch, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "project_set_archived"), Description("Archives or restores a Moyai project using optimistic concurrency.")]
    public Task<Project> ProjectSetArchived(string name, long expectedRevision, bool archived, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        projects.SetArchivedAsync(name, expectedRevision, archived, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "project_overview", ReadOnly = true), Description("Returns an aggregate Project view with open item counts, blockers, releases, and recent changes.")]
    public Task<ProjectOverview> ProjectOverview(string project, int recentLimit = 10, CancellationToken cancellationToken = default) => queries.GetOverviewAsync(project, recentLimit, cancellationToken);

    [McpServerTool(Name = "project_changes_since", ReadOnly = true), Description("Returns paged append-only Project events after the specified timestamp.")]
    public Task<ProjectChanges> ProjectChangesSince(string project, DateTimeOffset since, int offset = 0, int limit = 50, CancellationToken cancellationToken = default) => queries.GetChangesSinceAsync(project, since, offset, limit, cancellationToken);

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

    [McpServerTool(Name = "work_item_history", ReadOnly = true), Description("Lists append-only audit events associated with a work item.")]
    public Task<IReadOnlyList<Moyai.Domain.Events.ProjectEvent>> WorkItemHistory(string project, string key, CancellationToken cancellationToken = default) => collaboration.ListHistoryAsync(project, key, cancellationToken);

    [McpServerTool(Name = "item_search", ReadOnly = true), Description("Searches WorkItem titles, descriptions, and comments through SQLite FTS5 with optional filters and pagination.")]
    public Task<PagedResult<WorkItem>> ItemSearch(string project, string query, WorkItemType? type = null, string? status = null, WorkItemPriority? priority = null, string? owner = null, DateTimeOffset? createdAfter = null, DateTimeOffset? updatedAfter = null, int offset = 0, int limit = 50, CancellationToken cancellationToken = default) => queries.SearchAsync(new WorkItemSearchRequest(project, query, type, status, priority, owner, createdAfter, updatedAfter, offset, limit), cancellationToken);

    [McpServerTool(Name = "relation_add"), Description("Adds a WorkItem relation and returns a cycle warning when applicable.")]
    public Task<RelationAddResult> RelationAdd(string project, string sourceKey, string targetKey, string relation, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.AddRelationAsync(project, sourceKey, targetKey, relation, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "relation_remove"), Description("Removes a WorkItem relation and records an audit event.")]
    public Task<bool> RelationRemove(string project, Guid relationId, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.RemoveRelationAsync(project, relationId, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "relation_list", ReadOnly = true), Description("Lists relations involving a WorkItem.")]
    public Task<IReadOnlyList<WorkItemRelation>> RelationList(string project, string key, CancellationToken cancellationToken = default) => collaboration.ListRelationsAsync(project, key, cancellationToken);

    [McpServerTool(Name = "comment_add"), Description("Appends a persistent WorkItem comment.")]
    public Task<WorkItemComment> CommentAdd(string project, string key, string body, string authorType, string authorName, CancellationToken cancellationToken = default) => collaboration.AddCommentAsync(project, key, body, authorType, authorName, cancellationToken);

    [McpServerTool(Name = "comment_list", ReadOnly = true), Description("Lists persistent comments for a WorkItem.")]
    public Task<IReadOnlyList<WorkItemComment>> CommentList(string project, string key, CancellationToken cancellationToken = default) => collaboration.ListCommentsAsync(project, key, cancellationToken);

    [McpServerTool(Name = "task_link_add"), Description("Links a WorkItem to an external task such as Hataori.")]
    public Task<WorkItemTaskLink> TaskLinkAdd(string project, string key, string taskSystem, string taskId, string relation, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.AddTaskLinkAsync(project, key, taskSystem, taskId, relation, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "task_link_remove"), Description("Removes an external task link from a WorkItem.")]
    public Task<bool> TaskLinkRemove(string project, Guid linkId, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.RemoveTaskLinkAsync(project, linkId, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "task_link_list", ReadOnly = true), Description("Lists external task links for a WorkItem.")]
    public Task<IReadOnlyList<WorkItemTaskLink>> TaskLinkList(string project, string key, CancellationToken cancellationToken = default) => collaboration.ListTaskLinksAsync(project, key, cancellationToken);

    [McpServerTool(Name = "commit_link_add"), Description("Links a repository commit to a WorkItem.")]
    public Task<WorkItemCommitLink> CommitLinkAdd(string project, string key, string commitHash, string relation, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.AddCommitLinkAsync(project, key, commitHash, relation, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "commit_link_remove"), Description("Removes a repository commit link from a WorkItem.")]
    public Task<bool> CommitLinkRemove(string project, Guid linkId, string actorType, string actorName, CancellationToken cancellationToken = default) => collaboration.RemoveCommitLinkAsync(project, linkId, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "commit_link_list", ReadOnly = true), Description("Lists repository commit links for a WorkItem.")]
    public Task<IReadOnlyList<WorkItemCommitLink>> CommitLinkList(string project, string key, CancellationToken cancellationToken = default) => collaboration.ListCommitLinksAsync(project, key, cancellationToken);

    [McpServerTool(Name = "auth_introspect", ReadOnly = true), Description("Validates an internal Moyai service token for a provider audience and scope.")]
    public Task<AuthIntrospectionResult> AuthIntrospect(string token, string audience, string scope, CancellationToken cancellationToken = default) =>
        authentication.IntrospectAsync(token, audience, scope, cancellationToken);

    [McpServerTool(Name = "repository_status", ReadOnly = true), Description("Gets repository status through the Provider configured for a Moyai-managed project.")]
    public Task<RepositoryProviderResult> RepositoryStatus(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.Status, cancellationToken: cancellationToken));

    [McpServerTool(Name = "provider_version", ReadOnly = true), Description("Returns version information from the Repository Provider selected by the Project.")]
    public Task<RepositoryProviderResult> ProviderVersion(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.ProviderVersion, cancellationToken: cancellationToken));

    [McpServerTool(Name = "provider_capabilities", ReadOnly = true), Description("Returns Repository Provider capabilities for negotiation before optional operations.")]
    public Task<RepositoryProviderResult> ProviderCapabilities(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.ProviderCapabilities, cancellationToken: cancellationToken));

    [McpServerTool(Name = "repository_diff", ReadOnly = true), Description("Gets repository diff through the Provider configured for a Moyai-managed project.")]
    public Task<RepositoryProviderResult> RepositoryDiff(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.Diff, cancellationToken: cancellationToken));

    [McpServerTool(Name = "repository_commit"), Description("Commits a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryCommit(string project, string message, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.Commit, message, cancellationToken: cancellationToken));

    [McpServerTool(Name = "repository_push"), Description("Pushes a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryPush(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.Push, cancellationToken: cancellationToken));

    [McpServerTool(Name = "repository_pull"), Description("Pulls a Moyai-managed repository through its configured Provider using internal service authentication.")]
    public Task<RepositoryProviderResult> RepositoryPull(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.Pull, cancellationToken: cancellationToken));

    [McpServerTool(Name = "branch_list", ReadOnly = true), Description("Lists branches through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> BranchList(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.BranchList, cancellationToken: cancellationToken));

    [McpServerTool(Name = "branch_create"), Description("Creates a branch through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> BranchCreate(string project, string branch, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.BranchCreate, branch: branch, cancellationToken: cancellationToken));

    [McpServerTool(Name = "branch_delete", Destructive = true), Description("Deletes an allowed branch through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> BranchDelete(string project, string branch, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.BranchDelete, branch: branch, cancellationToken: cancellationToken));

    [McpServerTool(Name = "tag_create"), Description("Creates a tag through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> TagCreate(string project, string tag, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.TagCreate, tag: tag, cancellationToken: cancellationToken));

    [McpServerTool(Name = "tag_delete", Destructive = true), Description("Deletes an allowed tag through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> TagDelete(string project, string tag, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.TagDelete, tag: tag, cancellationToken: cancellationToken));

    [McpServerTool(Name = "tag_push"), Description("Pushes an existing local tag through the Project Repository Provider.")]
    public Task<RepositoryProviderResult> TagPush(string project, string tag, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => routing.ExecuteAsync(project, RepositoryOperation.TagPush, tag: tag, cancellationToken: cancellationToken));

    [McpServerTool(Name = "build", Destructive = true), Description("Builds a tracked clean repository commit through the configured Provider.")]
    public Task<Build> Build(string project, string actorType, string actorName, string configuration = "Release", CancellationToken cancellationToken = default) => WithConfigurationErrors(() => builds.StartAsync(project, configuration, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "build_start", Destructive = true), Description("Starts a tracked build from a clean repository commit.")]
    public Task<Build> BuildStart(string project, string actorType, string actorName, string configuration = "Release", CancellationToken cancellationToken = default) => WithConfigurationErrors(() => builds.StartAsync(project, configuration, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "build_get", ReadOnly = true), Description("Gets a tracked build.")]
    public Task<Build> BuildGet(string project, Guid buildId, CancellationToken cancellationToken = default) => builds.GetAsync(project, buildId, cancellationToken);

    [McpServerTool(Name = "build_list", ReadOnly = true), Description("Lists tracked builds newest first.")]
    public Task<IReadOnlyList<Build>> BuildList(string project, CancellationToken cancellationToken = default) => builds.ListAsync(project, cancellationToken);

    [McpServerTool(Name = "build_artifacts", ReadOnly = true), Description("Lists immutable artifacts for a build.")]
    public Task<IReadOnlyList<BuildArtifact>> BuildArtifacts(string project, Guid buildId, CancellationToken cancellationToken = default) => builds.ListArtifactsAsync(project, buildId, cancellationToken);

    [McpServerTool(Name = "build_clean", Destructive = true), Description("Removes artifact metadata for completed builds without deleting build history.")]
    public Task<LifecycleResult> BuildClean(string project, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => builds.CleanAsync(project, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_create"), Description("Creates a draft release in Moyai and records an audit event.")]
    public Task<Release> ReleaseCreate(string project, string version, ReleaseChannel channel, string actorType, string actorName, string? notes = null, CancellationToken cancellationToken = default) => releases.CreateAsync(new CreateReleaseCommand(project, version, channel, notes, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "release_get", ReadOnly = true), Description("Gets a Moyai release by project and version.")]
    public Task<Release> ReleaseGet(string project, string version, bool includeDeleted = false, CancellationToken cancellationToken = default) => releases.GetAsync(project, version, includeDeleted, cancellationToken);

    [McpServerTool(Name = "release_list", ReadOnly = true), Description("Lists releases managed by a Moyai project.")]
    public Task<IReadOnlyList<Release>> ReleaseList(string project, bool includeDeleted = false, CancellationToken cancellationToken = default) => releases.ListAsync(project, includeDeleted, cancellationToken);

    [McpServerTool(Name = "release_update"), Description("Updates editable release metadata using optimistic concurrency.")]
    public Task<Release> ReleaseUpdate(string project, string version, ReleaseChannel channel, long expectedRevision, string actorType, string actorName, string? tagName = null, string? commitHash = null, string? notes = null, DateTimeOffset? plannedAt = null, CancellationToken cancellationToken = default) => releases.UpdateAsync(new UpdateReleaseCommand(project, version, channel, tagName, commitHash, notes, plannedAt, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "release_transition"), Description("Transitions a release according to the v1 release workflow.")]
    public Task<Release> ReleaseTransition(string project, string version, ReleaseStatus nextStatus, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => releases.TransitionAsync(new TransitionReleaseCommand(project, version, nextStatus, expectedRevision, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "release_add_item"), Description("Adds a WorkItem to a Release with a typed relation.")]
    public Task<ReleaseWorkItem> ReleaseAddItem(string project, string version, string workItemKey, string relation, string actorType, string actorName, CancellationToken cancellationToken = default) => releaseContent.AddItemAsync(new AddReleaseItemCommand(project, version, workItemKey, relation, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "release_remove_item"), Description("Removes a WorkItem relation from a Release.")]
    public Task<bool> ReleaseRemoveItem(string project, string version, Guid relationId, string actorType, string actorName, CancellationToken cancellationToken = default) => releaseContent.RemoveItemAsync(project, version, relationId, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "release_list_items", ReadOnly = true), Description("Lists WorkItem relations for a Release.")]
    public Task<IReadOnlyList<ReleaseWorkItem>> ReleaseListItems(string project, string version, CancellationToken cancellationToken = default) => releaseContent.ListItemsAsync(project, version, cancellationToken);

    [McpServerTool(Name = "release_add_artifact"), Description("Adds distribution artifact metadata to a Release without storing file content.")]
    public Task<ReleaseArtifact> ReleaseAddArtifact(string project, string version, string name, string artifactType, string platform, string architecture, string fileName, string actorType, string actorName, Guid? buildArtifactId = null, string? filePath = null, string? downloadUrl = null, long? fileSize = null, string? sha256 = null, string? signaturePath = null, string? signatureUrl = null, CancellationToken cancellationToken = default) => releaseContent.AddArtifactAsync(new AddReleaseArtifactCommand(project, version, buildArtifactId, name, artifactType, platform, architecture, fileName, filePath, downloadUrl, fileSize, sha256, signaturePath, signatureUrl, actorType, actorName), cancellationToken);

    [McpServerTool(Name = "release_remove_artifact"), Description("Removes artifact metadata from a Release.")]
    public Task<bool> ReleaseRemoveArtifact(string project, string version, Guid artifactId, string actorType, string actorName, CancellationToken cancellationToken = default) => releaseContent.RemoveArtifactAsync(project, version, artifactId, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "release_list_artifacts", ReadOnly = true), Description("Lists artifact metadata for a Release.")]
    public Task<IReadOnlyList<ReleaseArtifact>> ReleaseListArtifacts(string project, string version, CancellationToken cancellationToken = default) => releaseContent.ListArtifactsAsync(project, version, cancellationToken);

    [McpServerTool(Name = "release_prepare"), Description("Moves a planned release into preparation.")]
    public Task<Release> ReleasePrepare(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.PrepareAsync(project, version, expectedRevision, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_mark_ready"), Description("Marks a prepared release as ready to publish.")]
    public Task<Release> ReleaseMarkReady(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.MarkReadyAsync(project, version, expectedRevision, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_publish", Destructive = true), Description("Publishes an existing ready release. Call only after explicit user approval for the exact project, version, and destination.")]
    public Task<ReleasePublishResult> ReleasePublish(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.PublishAsync(project, version, expectedRevision, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_retry", Destructive = true), Description("Retries a failed release publish idempotently. Requires explicit approval.")]
    public Task<ReleasePublishResult> ReleaseRetry(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.RetryAsync(project, version, expectedRevision, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_withdraw", Destructive = true), Description("Withdraws an existing release through the repository Provider.")]
    public Task<ReleasePublishResult> ReleaseWithdraw(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.WithdrawAsync(project, version, expectedRevision, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "release_latest", ReadOnly = true), Description("Gets the latest released stable release.")]
    public Task<Release?> ReleaseLatest(string project, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.LatestAsync(project, cancellationToken));

    [McpServerTool(Name = "release_overview", ReadOnly = true), Description("Gets a release with its WorkItem relations and artifact metadata.")]
    public Task<ReleaseOverview> ReleaseOverview(string project, string version, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => releaseOrchestration.OverviewAsync(project, version, cancellationToken));

    [McpServerTool(Name = "deployment_target_get", ReadOnly = true), Description("Gets the single Deployment Target for a Project.")]
    public Task<DeploymentTarget> DeploymentTargetGet(string project, CancellationToken cancellationToken = default) => deployments.GetTargetAsync(project, cancellationToken);

    [McpServerTool(Name = "deployment_target_update"), Description("Creates or updates the single Deployment Target for a Project.")]
    public Task<DeploymentTarget> DeploymentTargetUpdate(string project, string name, string mode, string destinationPath, long expectedRevision, string actorType, string actorName, string? kelpieTarget = null, string? configJson = null, CancellationToken cancellationToken = default) => deployments.UpdateTargetAsync(project, name, mode, destinationPath, kelpieTarget, configJson, expectedRevision, actorType, actorName, cancellationToken);

    [McpServerTool(Name = "deploy", Destructive = true), Description("Deploys a managed Build Artifact. Requires explicit approval for the exact target.")]
    public Task<Deployment> Deploy(string project, Guid buildId, Guid artifactId, string actorType, string actorName, string? version = null, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => deployments.StartAsync(project, buildId, artifactId, version, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "deploy_start", Destructive = true), Description("Starts a tracked Deployment from a managed Build Artifact.")]
    public Task<Deployment> DeployStart(string project, Guid buildId, Guid artifactId, string actorType, string actorName, string? version = null, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => deployments.StartAsync(project, buildId, artifactId, version, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "deploy_get", ReadOnly = true), Description("Gets a Deployment.")]
    public Task<Deployment> DeployGet(string project, Guid deploymentId, CancellationToken cancellationToken = default) => deployments.GetAsync(project, deploymentId, cancellationToken);

    [McpServerTool(Name = "deploy_list", ReadOnly = true), Description("Lists Deployments newest first.")]
    public Task<IReadOnlyList<Deployment>> DeployList(string project, CancellationToken cancellationToken = default) => deployments.ListAsync(project, cancellationToken);

    [McpServerTool(Name = "deploy_status", ReadOnly = true), Description("Gets persisted Deployment status.")]
    public Task<Deployment> DeployStatus(string project, Guid deploymentId, CancellationToken cancellationToken = default) => deployments.GetAsync(project, deploymentId, cancellationToken);

    [McpServerTool(Name = "deploy_retry", Destructive = true), Description("Retries a failed Deployment using the specified managed artifact.")]
    public Task<Deployment> DeployRetry(string project, Guid deploymentId, Guid artifactId, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => deployments.RetryAsync(project, deploymentId, artifactId, actorType, actorName, cancellationToken));

    [McpServerTool(Name = "deploy_rollback", Destructive = true), Description("Rolls back a succeeded Deployment and records rollback failure.")]
    public Task<Deployment> DeployRollback(string project, Guid deploymentId, string actorType, string actorName, CancellationToken cancellationToken = default) => WithConfigurationErrors(() => deployments.RollbackAsync(project, deploymentId, actorType, actorName, cancellationToken));
}
