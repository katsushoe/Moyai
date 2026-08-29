using System.Text.Json;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.Releases;

namespace Moyai.Application.Releases;

/// <summary>Releaseの作成、参照、更新、状態遷移を提供します。</summary>
public sealed class ReleaseService
{
    private readonly IProjectRepository _projects;
    private readonly IReleaseRepository _releases;
    private readonly TimeProvider _timeProvider;

    public ReleaseService(IProjectRepository projects, IReleaseRepository releases, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _projects = projects;
        _releases = releases;
        _timeProvider = timeProvider;
    }

    public async Task<Release> CreateAsync(CreateReleaseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Moyai.Domain.Projects.Project project = await GetProjectAsync(command.Project, cancellationToken).ConfigureAwait(false);
        Release release = Release.Create(project.Id, command.Version, command.Channel, command.ReleaseNotes, _timeProvider);
        await _releases.AddAsync(release, Event(release, "release_created", command.ActorType, command.ActorName, null), cancellationToken).ConfigureAwait(false);
        return release;
    }

    public async Task<Release> GetAsync(string projectName, string version, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        Moyai.Domain.Projects.Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _releases.GetAsync(project.Id, version, includeDeleted, cancellationToken).ConfigureAwait(false) ?? throw new ReleaseNotFoundException(version);
    }

    public async Task<IReadOnlyList<Release>> ListAsync(string projectName, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        Moyai.Domain.Projects.Project project = await GetProjectAsync(projectName, cancellationToken).ConfigureAwait(false);
        return await _releases.ListAsync(project.Id, includeDeleted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Release> UpdateAsync(UpdateReleaseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Release release = await GetAsync(command.Project, command.Version, cancellationToken: cancellationToken).ConfigureAwait(false);
        string before = JsonSerializer.Serialize(release);
        release.Update(command.Channel, command.TagName, command.CommitHash, command.ReleaseNotes, command.PlannedAt, _timeProvider);
        await _releases.UpdateAsync(release, command.ExpectedRevision, Event(release, "release_updated", command.ActorType, command.ActorName, before), cancellationToken).ConfigureAwait(false);
        return release;
    }

    public async Task<Release> TransitionAsync(TransitionReleaseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Release release = await GetAsync(command.Project, command.Version, cancellationToken: cancellationToken).ConfigureAwait(false);
        string before = JsonSerializer.Serialize(release);
        release.TransitionTo(command.NextStatus, _timeProvider);
        string eventType = command.NextStatus switch { ReleaseStatus.Ready => "release_ready", ReleaseStatus.Released => "release_published", ReleaseStatus.Withdrawn => "release_withdrawn", _ => "release_updated" };
        await _releases.UpdateAsync(release, command.ExpectedRevision, Event(release, eventType, command.ActorType, command.ActorName, before), cancellationToken).ConfigureAwait(false);
        return release;
    }

    private async Task<Moyai.Domain.Projects.Project> GetProjectAsync(string name, CancellationToken cancellationToken) =>
        await _projects.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(name);

    private ProjectEvent Event(Release release, string type, string actorType, string actorName, string? before) =>
        ProjectEvent.Create(release.ProjectId, "release", release.Id, type, actorType, actorName, before, JsonSerializer.Serialize(release), null, _timeProvider);
}
