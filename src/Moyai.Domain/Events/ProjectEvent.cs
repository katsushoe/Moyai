namespace Moyai.Domain.Events;

/// <summary>Project Stateへの追記専用監査イベントを表します。</summary>
public sealed record ProjectEvent(Guid Id, Guid ProjectId, string EntityType, Guid EntityId, string EventType, string ActorType, string ActorName, string? BeforeJson, string? AfterJson, string? Message, DateTimeOffset CreatedAt)
{
    /// <summary>監査イベントを生成します。</summary>
    public static ProjectEvent Create(Guid projectId, string entityType, Guid entityId, string eventType, string actorType, string actorName, string? beforeJson, string? afterJson, string? message, TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (entityId == Guid.Empty) throw new ArgumentException("Entity ID is required.", nameof(entityId));
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new ProjectEvent(Guid.NewGuid(), projectId, entityType, entityId, eventType, actorType, actorName, beforeJson, afterJson, message, timeProvider.GetUtcNow());
    }
}
