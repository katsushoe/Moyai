using System.Text.Json;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;

namespace Moyai.Application.Projects;

/// <summary>Project StateのApplication操作を提供します。</summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ProjectService(IProjectRepository repository, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Project> CreateAsync(CreateProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Project project = Project.Create(command.Name, command.SourcePath, command.InstallPath, command.RepositoryUrl, command.RepositoryProvider, command.BuildProvider, command.DeployMode, _timeProvider);
        ProjectEvent projectEvent = CreateEvent(project, "project_created", command.ActorType, command.ActorName, null, JsonSerializer.Serialize(project));
        await _repository.AddAsync(project, projectEvent, cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task<Project> GetAsync(string name, CancellationToken cancellationToken = default) =>
        await _repository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(name);

    public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        _repository.ListAsync(includeArchived, cancellationToken);

    public async Task<Project> UpdateAsync(UpdateProjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Project project = await GetAsync(command.CurrentName, cancellationToken).ConfigureAwait(false);
        string beforeJson = JsonSerializer.Serialize(project);
        project.Update(command.Name, command.Description, command.BuildConfigJson, command.GitUserName, command.GitUserEmail, command.GitRemoteName, command.GitDefaultBranch, _timeProvider);
        ProjectEvent projectEvent = CreateEvent(project, "project_updated", command.ActorType, command.ActorName, beforeJson, JsonSerializer.Serialize(project));
        await _repository.UpdateAsync(project, command.ExpectedRevision, projectEvent, cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task<Project> SetArchivedAsync(string name, long expectedRevision, bool archived, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Project project = await GetAsync(name, cancellationToken).ConfigureAwait(false);
        string beforeJson = JsonSerializer.Serialize(project);
        if (archived) project.Archive(_timeProvider); else project.Restore(_timeProvider);
        string eventType = archived ? "project_archived" : "project_restored";
        ProjectEvent projectEvent = CreateEvent(project, eventType, actorType, actorName, beforeJson, JsonSerializer.Serialize(project));
        await _repository.UpdateAsync(project, expectedRevision, projectEvent, cancellationToken).ConfigureAwait(false);
        return project;
    }

    private ProjectEvent CreateEvent(Project project, string eventType, string actorType, string actorName, string? beforeJson, string? afterJson) =>
        ProjectEvent.Create(project.Id, "project", project.Id, eventType, actorType, actorName, beforeJson, afterJson, null, _timeProvider);
}
