using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

public sealed record UpdateWorkItemCommand(
    string Project,
    string Key,
    string Title,
    string? Description,
    WorkItemPriority Priority,
    WorkItemSeverity? Severity,
    string? Owner,
    string? MetadataJson,
    long ExpectedRevision,
    string ActorType,
    string ActorName);
