namespace Moyai.Domain.Builds;

/// <summary>ProjectのBuild実行状態です。</summary>
public sealed class Build
{
    private Build(Guid id, Guid projectId, string provider, string sourceCommit, string configuration, string? configJson, string actorType, string actorName, DateTimeOffset createdAt)
    {
        Id = id; ProjectId = projectId; Provider = provider; SourceCommit = sourceCommit; Configuration = configuration; ConfigJson = configJson; ActorType = actorType; ActorName = actorName; CreatedAt = createdAt; Status = BuildStatus.Queued; Revision = 1;
    }

    /// <summary>Queued Buildを作成します。</summary>
    public static Build Create(Guid projectId, string provider, string sourceCommit, string configuration, string? configJson, string actorType, string actorName, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(provider); ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit); ArgumentException.ThrowIfNullOrWhiteSpace(configuration); ArgumentException.ThrowIfNullOrWhiteSpace(actorType); ArgumentException.ThrowIfNullOrWhiteSpace(actorName); ArgumentNullException.ThrowIfNull(timeProvider);
        return new Build(Guid.NewGuid(), projectId, provider, sourceCommit, configuration, configJson, actorType, actorName, timeProvider.GetUtcNow());
    }

    /// <summary>永続状態を復元します。</summary>
    public static Build Restore(Guid id, Guid projectId, string provider, BuildStatus status, string sourceCommit, string configuration, string? configJson, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string actorType, string actorName, string? errorCode, string? errorMessage, DateTimeOffset createdAt, long revision) =>
        new(id, projectId, provider, sourceCommit, configuration, configJson, actorType, actorName, createdAt) { Status = status, StartedAt = startedAt, FinishedAt = finishedAt, ErrorCode = errorCode, ErrorMessage = errorMessage, Revision = revision };

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Provider { get; }
    public BuildStatus Status { get; private set; }
    public string SourceCommit { get; }
    public string Configuration { get; }
    public string? ConfigJson { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string ActorType { get; }
    public string ActorName { get; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public long Revision { get; private set; }

    /// <summary>Build開始を記録します。</summary>
    public void Start(TimeProvider timeProvider) { Ensure(BuildStatus.Queued); Status = BuildStatus.Building; StartedAt = timeProvider.GetUtcNow(); Revision++; }
    /// <summary>Build成功を記録します。</summary>
    public void Succeed(TimeProvider timeProvider) { Ensure(BuildStatus.Building); Status = BuildStatus.Succeeded; FinishedAt = timeProvider.GetUtcNow(); Revision++; }
    /// <summary>Build失敗を記録します。</summary>
    public void Fail(string? code, string? message, TimeProvider timeProvider) { Ensure(BuildStatus.Building); Status = BuildStatus.Failed; ErrorCode = code; ErrorMessage = message; FinishedAt = timeProvider.GetUtcNow(); Revision++; }
    private void Ensure(BuildStatus expected) { if (Status != expected) throw new InvalidOperationException($"Build status must be {expected}."); }
}

public enum BuildStatus { Queued, Preparing, Building, Succeeded, Failed, Cancelled }

/// <summary>Buildから生成された不変Artifactです。</summary>
public sealed record BuildArtifact(Guid Id, Guid ProjectId, Guid BuildId, string Name, string ArtifactType, string ArtifactKind, string FilePath, long? FileSize, string? Sha256, string? ManifestSha256, DateTimeOffset CreatedAt);
