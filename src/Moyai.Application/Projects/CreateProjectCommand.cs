namespace Moyai.Application.Projects;

public sealed record CreateProjectCommand(
    string Name,
    string SourcePath = "",
    string? InstallPath = null,
    string RepositoryUrl = "",
    string? RepositoryProvider = null,
    string BuildProvider = "",
    string DeployMode = "",
    string ActorType = "client",
    string ActorName = "unspecified");
