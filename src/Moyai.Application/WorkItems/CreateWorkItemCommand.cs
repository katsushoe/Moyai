using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

public sealed record CreateWorkItemCommand(
    string Project,
    WorkItemType Type,
    string Title,
    string ActorType,
    string ActorName);
