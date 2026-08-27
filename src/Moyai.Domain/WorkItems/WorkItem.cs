namespace Moyai.Domain.WorkItems;

/// <summary>プロジェクト内の課題、要求または判断を表します。</summary>
public sealed class WorkItem
{
    /// <summary>新しい作業項目を生成します。</summary>
    public static WorkItem Create(Guid projectId, WorkItemType type, long sequenceNumber, string title, string actorType, string actorName, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset now = timeProvider.GetUtcNow();
        return new WorkItem(Guid.NewGuid(), projectId, BuildKey(type, sequenceNumber), sequenceNumber, type, title, WorkItemWorkflow.GetInitialStatus(type), actorType, actorName, now);
    }

    private WorkItem(Guid id, Guid projectId, string key, long sequenceNumber, WorkItemType type, string title, string status, string createdByType, string createdByName, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Key = key;
        SequenceNumber = sequenceNumber;
        Type = type;
        Title = title;
        Status = status;
        Priority = WorkItemPriority.Normal;
        CreatedByType = createdByType;
        CreatedByName = createdByName;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Revision = 1;
    }

    /// <summary>永続化されたWorkItemを復元します。</summary>
    public static WorkItem RestoreState(Guid id, Guid projectId, string key, long sequenceNumber, WorkItemType type, string title, string? description, string status, WorkItemPriority priority, WorkItemSeverity? severity, string? owner, string? metadataJson, string createdByType, string createdByName, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset? closedAt, DateTimeOffset? deletedAt, long revision)
    {
        var item = new WorkItem(id, projectId, key, sequenceNumber, type, title, status, createdByType, createdByName, createdAt)
        {
            Description = description,
            Priority = priority,
            Severity = severity,
            Owner = owner,
            MetadataJson = metadataJson,
            UpdatedAt = updatedAt,
            ClosedAt = closedAt,
            DeletedAt = deletedAt,
            Revision = revision,
        };
        return item;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Key { get; }
    public long SequenceNumber { get; }
    public WorkItemType Type { get; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public string Status { get; private set; }
    public WorkItemPriority Priority { get; private set; }
    public WorkItemSeverity? Severity { get; private set; }
    public string? Owner { get; private set; }
    public string? MetadataJson { get; private set; }
    public string CreatedByType { get; }
    public string CreatedByName { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Status以外の編集可能フィールドを更新します。</summary>
    public void Update(string title, string? description, WorkItemPriority priority, WorkItemSeverity? severity, string? owner, string? metadataJson, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Title = title;
        Description = description;
        Priority = priority;
        Severity = severity;
        Owner = owner;
        MetadataJson = metadataJson;
        Touch(timeProvider);
    }

    /// <summary>作業項目をSoft Deleteします。</summary>
    public void Delete(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        DeletedAt = timeProvider.GetUtcNow();
        UpdatedAt = DeletedAt.Value;
        Revision++;
    }

    /// <summary>Soft Deleteされた作業項目を復元します。</summary>
    public void Restore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        DeletedAt = null;
        Touch(timeProvider);
    }

    /// <summary>仕様で許可された次状態へ遷移します。</summary>
    public void TransitionTo(string nextStatus, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextStatus);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!WorkItemWorkflow.CanTransition(Type, Status, nextStatus))
        {
            throw new InvalidWorkItemTransitionException(Type, Status, nextStatus);
        }

        Status = nextStatus;
        UpdatedAt = timeProvider.GetUtcNow();
        ClosedAt = string.Equals(nextStatus, "closed", StringComparison.Ordinal) ? UpdatedAt : null;
        Revision++;
    }

    private void Touch(TimeProvider timeProvider)
    {
        UpdatedAt = timeProvider.GetUtcNow();
        Revision++;
    }

    private static string BuildKey(WorkItemType type, long sequenceNumber)
    {
        string prefix = type switch
        {
            WorkItemType.Issue => "ISSUE",
            WorkItemType.Bug => "BUG",
            WorkItemType.ChangeRequest => "CR",
            WorkItemType.Feature => "FEAT",
            WorkItemType.Risk => "RISK",
            WorkItemType.Decision => "DEC",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        return $"{prefix}-{sequenceNumber}";
    }
}
