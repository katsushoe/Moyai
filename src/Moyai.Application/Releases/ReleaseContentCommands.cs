namespace Moyai.Application.Releases;

public sealed record AddReleaseItemCommand(string Project, string Version, string WorkItemKey, string Relation, string ActorType, string ActorName);
public sealed record AddReleaseArtifactCommand(string Project, string Version, Guid? BuildArtifactId, string Name, string ArtifactType, string Platform, string Architecture, string FileName, string? FilePath, string? DownloadUrl, long? FileSize, string? Sha256, string? SignaturePath, string? SignatureUrl, string ActorType, string ActorName);
