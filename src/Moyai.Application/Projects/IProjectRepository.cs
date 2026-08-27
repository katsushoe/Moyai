using Moyai.Domain.Events;
using Moyai.Domain.Projects;

namespace Moyai.Application.Projects;

public interface IProjectRepository
{
    Task AddAsync(Project project, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<Project?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);
    Task UpdateAsync(Project project, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
}
