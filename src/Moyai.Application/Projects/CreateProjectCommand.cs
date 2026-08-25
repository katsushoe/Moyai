namespace Moyai.Application.Projects;

public sealed record CreateProjectCommand(
    string Name,
    string SourcePath,
    string? InstallPath,
    string RepositoryUrl,
    string? RepositoryProvider,
    string BuildProvider,
    string DeployMode,
    string ActorType,
    string ActorName);
