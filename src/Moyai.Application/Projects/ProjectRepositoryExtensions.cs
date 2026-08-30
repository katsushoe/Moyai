using Moyai.Domain.Projects;

namespace Moyai.Application.Projects;

public static class ProjectRepositoryExtensions
{
    public static async Task<Project> GetRequiredAsync(this IProjectRepository repository, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Project? project = await repository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (project is not null) return project;

        IReadOnlyList<Project> registered = await repository.ListAsync(true, cancellationToken).ConfigureAwait(false);
        throw new ProjectNotFoundException(name, registered.Select(static value => value.Name).ToArray());
    }
}
