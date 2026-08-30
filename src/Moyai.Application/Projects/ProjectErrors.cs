namespace Moyai.Application.Projects;

public sealed class ProjectNotFoundException : InvalidOperationException
{
    public ProjectNotFoundException(string name, IReadOnlyList<string> candidates)
        : base(CreateMessage(name, candidates))
    {
        ProjectName = name;
        Candidates = candidates;
    }

    public string ProjectName { get; }

    public IReadOnlyList<string> Candidates { get; }

    private static string CreateMessage(string name, IReadOnlyList<string> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(candidates);
        string suffix = candidates.Count == 0 ? " No projects are registered." : $" Registered project candidates: {string.Join(", ", candidates)}.";
        return $"Project '{name}' was not found.{suffix}";
    }
}
public sealed class ProjectNameConflictException(string name) : InvalidOperationException($"Project '{name}' already exists.");
public sealed class RevisionConflictException(long expectedRevision) : InvalidOperationException($"Revision {expectedRevision} is no longer current.");
