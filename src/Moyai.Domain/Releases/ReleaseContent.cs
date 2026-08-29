namespace Moyai.Domain.Releases;

/// <summary>ReleaseとWorkItemの関連です。</summary>
public sealed record ReleaseWorkItem(Guid Id, Guid ProjectId, Guid ReleaseId, Guid WorkItemId, string Relation, DateTimeOffset CreatedAt)
{
    private static readonly HashSet<string> Relations = new(StringComparer.Ordinal) { "includes", "fixes", "implements", "resolves" };

    public static ReleaseWorkItem Create(Guid projectId, Guid releaseId, Guid workItemId, string relation, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty || releaseId == Guid.Empty || workItemId == Guid.Empty) throw new ArgumentException("Project, Release, and WorkItem IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        string normalized = relation.ToLowerInvariant();
        if (!Relations.Contains(normalized)) throw new ArgumentException($"Unsupported Release WorkItem relation '{relation}'.", nameof(relation));
        return new ReleaseWorkItem(Guid.NewGuid(), projectId, releaseId, workItemId, normalized, timeProvider.GetUtcNow());
    }
}

/// <summary>Release配布物のMetadataです。</summary>
public sealed record ReleaseArtifact(Guid Id, Guid ProjectId, Guid ReleaseId, Guid? BuildArtifactId, string Name, string ArtifactType, string Platform, string Architecture, string FileName, string? FilePath, string? DownloadUrl, long? FileSize, string? Sha256, string? SignaturePath, string? SignatureUrl, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "installer", "portable", "archive", "package", "symbols", "source", "update", "documentation", "other" };
    private static readonly HashSet<string> Platforms = new(StringComparer.Ordinal) { "windows", "macos", "linux", "android", "ios", "any" };
    private static readonly HashSet<string> Architectures = new(StringComparer.Ordinal) { "x64", "arm64", "x86", "universal", "any" };

    public static ReleaseArtifact Create(Guid projectId, Guid releaseId, Guid? buildArtifactId, string name, string artifactType, string platform, string architecture, string fileName, string? filePath, string? downloadUrl, long? fileSize, string? sha256, string? signaturePath, string? signatureUrl, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty || releaseId == Guid.Empty) throw new ArgumentException("Project and Release IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(timeProvider);
        string type = Normalize(artifactType, Types, nameof(artifactType));
        string targetPlatform = Normalize(platform, Platforms, nameof(platform));
        string targetArchitecture = Normalize(architecture, Architectures, nameof(architecture));
        if (fileSize < 0) throw new ArgumentOutOfRangeException(nameof(fileSize));
        DateTimeOffset now = timeProvider.GetUtcNow();
        return new ReleaseArtifact(Guid.NewGuid(), projectId, releaseId, buildArtifactId, name, type, targetPlatform, targetArchitecture, fileName, filePath, downloadUrl, fileSize, sha256, signaturePath, signatureUrl, now, now);
    }

    private static string Normalize(string value, HashSet<string> allowed, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.ToLowerInvariant();
        if (!allowed.Contains(normalized)) throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
        return normalized;
    }
}
