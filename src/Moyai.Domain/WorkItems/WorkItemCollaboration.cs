namespace Moyai.Domain.WorkItems;

/// <summary>WorkItem間の関係を表します。</summary>
public sealed record WorkItemRelation(Guid Id, Guid ProjectId, Guid SourceWorkItemId, Guid TargetWorkItemId, string Relation, DateTimeOffset CreatedAt)
{
    private static readonly HashSet<string> AllowedRelations = ["relates_to", "depends_on", "blocks", "duplicates", "caused_by", "implements", "supersedes"];

    public static WorkItemRelation Create(Guid projectId, Guid sourceWorkItemId, Guid targetWorkItemId, string relation, TimeProvider timeProvider)
    {
        ValidateIds(projectId, sourceWorkItemId, targetWorkItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        string normalized = relation.ToLowerInvariant();
        if (!AllowedRelations.Contains(normalized)) throw new ArgumentException($"Unsupported WorkItem relation '{relation}'.", nameof(relation));
        return new WorkItemRelation(Guid.NewGuid(), projectId, sourceWorkItemId, targetWorkItemId, normalized, timeProvider.GetUtcNow());
    }

    private static void ValidateIds(Guid projectId, Guid sourceWorkItemId, Guid targetWorkItemId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (sourceWorkItemId == Guid.Empty) throw new ArgumentException("Source WorkItem ID is required.", nameof(sourceWorkItemId));
        if (targetWorkItemId == Guid.Empty) throw new ArgumentException("Target WorkItem ID is required.", nameof(targetWorkItemId));
        if (sourceWorkItemId == targetWorkItemId) throw new ArgumentException("A WorkItem cannot relate to itself.", nameof(targetWorkItemId));
    }
}

/// <summary>WorkItemへ追記されるCommentを表します。</summary>
public sealed record WorkItemComment(Guid Id, Guid ProjectId, Guid WorkItemId, string Body, string AuthorType, string AuthorName, DateTimeOffset CreatedAt)
{
    public static WorkItemComment Create(Guid projectId, Guid workItemId, string body, string authorType, string authorName, TimeProvider timeProvider)
    {
        Validate(projectId, workItemId, body, authorType, authorName, timeProvider);
        return new WorkItemComment(Guid.NewGuid(), projectId, workItemId, body, authorType, authorName, timeProvider.GetUtcNow());
    }

    private static void Validate(Guid projectId, Guid workItemId, string body, string authorType, string authorName, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (workItemId == Guid.Empty) throw new ArgumentException("WorkItem ID is required.", nameof(workItemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorName);
        ArgumentNullException.ThrowIfNull(timeProvider);
    }
}

/// <summary>WorkItemと外部Taskの関連を表します。</summary>
public sealed record WorkItemTaskLink(Guid Id, Guid ProjectId, Guid WorkItemId, string TaskSystem, string TaskId, string Relation, DateTimeOffset CreatedAt)
{
    public static WorkItemTaskLink Create(Guid projectId, Guid workItemId, string taskSystem, string taskId, string relation, TimeProvider timeProvider)
    {
        ValidateLink(projectId, workItemId, taskSystem, taskId, relation, timeProvider);
        return new WorkItemTaskLink(Guid.NewGuid(), projectId, workItemId, taskSystem.ToLowerInvariant(), taskId, relation.ToLowerInvariant(), timeProvider.GetUtcNow());
    }

    private static void ValidateLink(Guid projectId, Guid workItemId, string first, string second, string relation, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (workItemId == Guid.Empty) throw new ArgumentException("WorkItem ID is required.", nameof(workItemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentNullException.ThrowIfNull(timeProvider);
    }
}

/// <summary>WorkItemとRepository Commitの関連を表します。</summary>
public sealed record WorkItemCommitLink(Guid Id, Guid ProjectId, Guid WorkItemId, string CommitHash, string Relation, DateTimeOffset CreatedAt)
{
    private static readonly HashSet<string> AllowedRelations = ["implements", "fixes", "relates_to"];

    public static WorkItemCommitLink Create(Guid projectId, Guid workItemId, string commitHash, string relation, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (workItemId == Guid.Empty) throw new ArgumentException("WorkItem ID is required.", nameof(workItemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        string normalized = relation.ToLowerInvariant();
        if (!AllowedRelations.Contains(normalized)) throw new ArgumentException($"Unsupported commit relation '{relation}'.", nameof(relation));
        return new WorkItemCommitLink(Guid.NewGuid(), projectId, workItemId, commitHash, normalized, timeProvider.GetUtcNow());
    }
}
