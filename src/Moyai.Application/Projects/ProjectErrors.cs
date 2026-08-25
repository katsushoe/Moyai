namespace Moyai.Application.Projects;

public sealed class ProjectNotFoundException(string name) : InvalidOperationException($"Project '{name}' was not found.");
public sealed class ProjectNameConflictException(string name) : InvalidOperationException($"Project '{name}' already exists.");
public sealed class RevisionConflictException(long expectedRevision) : InvalidOperationException($"Revision {expectedRevision} is no longer current.");
