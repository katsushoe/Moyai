namespace Moyai.Domain.Releases;

/// <summary>ProjectのRelease履歴と公開状態を表します。</summary>
public sealed class Release
{
    private static readonly HashSet<(ReleaseStatus Current, ReleaseStatus Next)> AllowedTransitions = new()
    {
        (ReleaseStatus.Draft, ReleaseStatus.Planned),
        (ReleaseStatus.Planned, ReleaseStatus.Preparing),
        (ReleaseStatus.Preparing, ReleaseStatus.Ready),
        (ReleaseStatus.Ready, ReleaseStatus.Publishing),
        (ReleaseStatus.Publishing, ReleaseStatus.Released),
        (ReleaseStatus.Publishing, ReleaseStatus.Failed),
        (ReleaseStatus.Failed, ReleaseStatus.Preparing),
        (ReleaseStatus.Failed, ReleaseStatus.Ready),
        (ReleaseStatus.Released, ReleaseStatus.Withdrawn),
    };

    private Release(Guid id, Guid projectId, string version, ReleaseChannel channel, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Version = version;
        Channel = channel;
        Status = ReleaseStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Revision = 1;
    }

    /// <summary>Draft状態のReleaseを作成します。</summary>
    public static Release Create(Guid projectId, string version, ReleaseChannel channel, string? releaseNotes, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(timeProvider);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return new Release(Guid.NewGuid(), projectId, version, channel, now) { ReleaseNotes = releaseNotes };
    }

    /// <summary>永続化されたReleaseを復元します。</summary>
    public static Release RestoreState(Guid id, Guid projectId, string version, ReleaseChannel channel, ReleaseStatus status, string? tagName, string? commitHash, string? releaseNotes, DateTimeOffset? plannedAt, DateTimeOffset? releasedAt, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset? deletedAt, long revision) =>
        new(id, projectId, version, channel, createdAt) { Status = status, TagName = tagName, CommitHash = commitHash, ReleaseNotes = releaseNotes, PlannedAt = plannedAt, ReleasedAt = releasedAt, UpdatedAt = updatedAt, DeletedAt = deletedAt, Revision = revision };

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Version { get; }
    public ReleaseChannel Channel { get; private set; }
    public ReleaseStatus Status { get; private set; }
    public string? TagName { get; private set; }
    public string? CommitHash { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public DateTimeOffset? PlannedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public long Revision { get; private set; }

    /// <summary>状態以外の編集可能なRelease情報を更新します。</summary>
    public void Update(ReleaseChannel channel, string? tagName, string? commitHash, string? releaseNotes, DateTimeOffset? plannedAt, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        Channel = channel;
        TagName = tagName;
        CommitHash = commitHash;
        ReleaseNotes = releaseNotes;
        PlannedAt = plannedAt;
        Touch(timeProvider);
    }

    /// <summary>仕様で許可された次状態へ遷移します。</summary>
    public void TransitionTo(ReleaseStatus next, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!AllowedTransitions.Contains((Status, next))) throw new InvalidReleaseTransitionException(Status, next);
        Status = next;
        DateTimeOffset now = timeProvider.GetUtcNow();
        UpdatedAt = now;
        if (next == ReleaseStatus.Released) ReleasedAt = now;
        Revision++;
    }

    private void Touch(TimeProvider timeProvider)
    {
        UpdatedAt = timeProvider.GetUtcNow();
        Revision++;
    }
}
