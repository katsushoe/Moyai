namespace Moyai.Domain.Deployments;

/// <summary>Project固有のDeployment Targetです。</summary>
public sealed record DeploymentTarget(Guid Id, Guid ProjectId, string Name, string Mode, string DestinationPath, string? KelpieTarget, string? ConfigJson, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision)
{
    public static DeploymentTarget Create(Guid projectId, string name, string mode, string destinationPath, string? kelpieTarget, string? configJson, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(mode); ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath); ArgumentNullException.ThrowIfNull(timeProvider);
        string normalized = mode.ToLowerInvariant(); if (normalized is not ("local" or "server")) throw new ArgumentException("Deployment mode must be local or server.", nameof(mode)); if (normalized == "server") ArgumentException.ThrowIfNullOrWhiteSpace(kelpieTarget); if (normalized == "local" && kelpieTarget is not null) throw new ArgumentException("Local target cannot specify Kelpie target.", nameof(kelpieTarget));
        DateTimeOffset now = timeProvider.GetUtcNow(); return new(Guid.NewGuid(), projectId, name, normalized, destinationPath, kelpieTarget, configJson, now, now, 1);
    }
}

/// <summary>Deployment実行とRollback状態です。</summary>
public sealed class Deployment
{
    private Deployment(Guid id, Guid projectId, Guid targetId, Guid buildId, Guid? releaseId, string mode, string sourceCommit, string destinationPath, string? kelpieTarget, Guid? previousId, Guid? rollbackOfId, string actorType, string actorName, DateTimeOffset createdAt) { Id = id; ProjectId = projectId; DeploymentTargetId = targetId; BuildId = buildId; ReleaseId = releaseId; Mode = mode; SourceCommit = sourceCommit; DestinationPath = destinationPath; KelpieTarget = kelpieTarget; PreviousDeploymentId = previousId; RollbackOfDeploymentId = rollbackOfId; ActorType = actorType; ActorName = actorName; CreatedAt = createdAt; Status = DeploymentStatus.Pending; Revision = 1; }
    public static Deployment Create(Guid projectId, DeploymentTarget target, Guid buildId, Guid? releaseId, string sourceCommit, Guid? previousId, Guid? rollbackOfId, string actorType, string actorName, TimeProvider timeProvider) => new(Guid.NewGuid(), projectId, target.Id, buildId, releaseId, target.Mode, sourceCommit, target.DestinationPath, target.KelpieTarget, previousId, rollbackOfId, actorType, actorName, timeProvider.GetUtcNow());
    public static Deployment Restore(Guid id, Guid projectId, Guid targetId, Guid buildId, Guid? releaseId, string mode, DeploymentStatus status, string sourceCommit, string destinationPath, string? kelpieTarget, Guid? previousId, Guid? rollbackOfId, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string actorType, string actorName, string? errorCode, string? errorMessage, DateTimeOffset createdAt, long revision) => new(id, projectId, targetId, buildId, releaseId, mode, sourceCommit, destinationPath, kelpieTarget, previousId, rollbackOfId, actorType, actorName, createdAt) { Status = status, StartedAt = startedAt, FinishedAt = finishedAt, ErrorCode = errorCode, ErrorMessage = errorMessage, Revision = revision };
    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid DeploymentTargetId { get; }
    public Guid BuildId { get; }
    public Guid? ReleaseId { get; }
    public string Mode { get; }
    public DeploymentStatus Status { get; private set; }
    public string SourceCommit { get; }
    public string DestinationPath { get; }
    public string? KelpieTarget { get; }
    public Guid? PreviousDeploymentId { get; }
    public Guid? RollbackOfDeploymentId { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string ActorType { get; }
    public string ActorName { get; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public long Revision { get; private set; }
    public void Transition(DeploymentStatus status, TimeProvider timeProvider, string? errorCode = null, string? errorMessage = null) { ArgumentNullException.ThrowIfNull(timeProvider); Status = status; DateTimeOffset now = timeProvider.GetUtcNow(); if (StartedAt is null) StartedAt = now; if (status is DeploymentStatus.Succeeded or DeploymentStatus.Failed or DeploymentStatus.RolledBack or DeploymentStatus.RollbackFailed) FinishedAt = now; ErrorCode = errorCode; ErrorMessage = errorMessage; Revision++; }
}

public enum DeploymentStatus { Pending, Preparing, Deploying, Verifying, Succeeded, Failed, RollingBack, RolledBack, RollbackFailed }
