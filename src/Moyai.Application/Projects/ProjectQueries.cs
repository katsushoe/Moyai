using Moyai.Domain.Events;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.Projects;

public sealed record WorkItemSearchRequest(string Project, string Query, WorkItemType? Type = null, string? Status = null, WorkItemPriority? Priority = null, string? Owner = null, DateTimeOffset? CreatedAfter = null, DateTimeOffset? UpdatedAfter = null, int Offset = 0, int Limit = 50);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Offset, int Limit, long Total);
public sealed record ProjectOverview(Project Project, IReadOnlyDictionary<string, long> OpenWorkItems, IReadOnlyList<WorkItem> Blockers, string? LatestStableRelease, string? PlannedRelease, IReadOnlyList<ProjectEvent> RecentlyChanged);
public sealed record ProjectChanges(IReadOnlyList<ProjectEvent> Events, DateTimeOffset Since, int Offset, int Limit, long Total);

public interface IProjectQueryRepository
{
    Task<PagedResult<WorkItem>> SearchAsync(Guid projectId, WorkItemSearchRequest request, CancellationToken cancellationToken = default);
    Task<ProjectOverview> GetOverviewAsync(Project project, int recentLimit, CancellationToken cancellationToken = default);
    Task<ProjectChanges> GetChangesSinceAsync(Guid projectId, DateTimeOffset since, int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>検索とProject集約Queryを提供します。</summary>
public sealed class ProjectQueryService
{
    private readonly IProjectRepository _projects;
    private readonly IProjectQueryRepository _queries;

    public ProjectQueryService(IProjectRepository projects, IProjectQueryRepository queries)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(queries);
        _projects = projects;
        _queries = queries;
    }

    public async Task<PagedResult<WorkItem>> SearchAsync(WorkItemSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ValidatePage(request.Offset, request.Limit);
        Project project = await GetProjectAsync(request.Project, cancellationToken).ConfigureAwait(false);
        return await _queries.SearchAsync(project.Id, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectOverview> GetOverviewAsync(string projectName, int recentLimit = 10, CancellationToken cancellationToken = default)
    {
        if (recentLimit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(recentLimit));
        Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _queries.GetOverviewAsync(project, recentLimit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectChanges> GetChangesSinceAsync(string projectName, DateTimeOffset since, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);
        Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _queries.GetChangesSinceAsync(project.Id, since, offset, limit, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Project> GetProjectAsync(string name, CancellationToken cancellationToken) =>
        await _projects.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(name);

    private static void ValidatePage(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
