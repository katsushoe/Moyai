using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Application.WorkItems;

public interface IWorkItemRepository
{
    Task<WorkItem> AddAsync(Guid projectId, WorkItemType type, Func<long, WorkItem> itemFactory, Func<WorkItem, ProjectEvent> eventFactory, CancellationToken cancellationToken = default);
    Task<WorkItem?> GetAsync(Guid projectId, string key, bool includeDeleted, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItem>> ListAsync(Guid projectId, bool includeDeleted, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkItem item, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
}
