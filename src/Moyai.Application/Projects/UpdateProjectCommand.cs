namespace Moyai.Application.Projects;

public sealed record UpdateProjectCommand(
    string CurrentName,
    string Name,
    string? Description,
    string? BuildConfigJson,
    string? GitUserName,
    string? GitUserEmail,
    string GitRemoteName,
    string? GitDefaultBranch,
    long ExpectedRevision,
    string ActorType,
    string ActorName);
