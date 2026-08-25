using Moyai.Domain.Events;

namespace Moyai.Domain.Tests.Events;

public sealed class ProjectEventTests
{
    [Fact]
    public void CreatePreservesAuditData()
    {
        Guid projectId = Guid.NewGuid();
        Guid entityId = Guid.NewGuid();
        ProjectEvent projectEvent = ProjectEvent.Create(projectId, "work_item", entityId, "item_created", "agent", "codex", null, "{}", "Created", TimeProvider.System);
        Assert.Equal(projectId, projectEvent.ProjectId);
        Assert.Equal(entityId, projectEvent.EntityId);
        Assert.Equal("item_created", projectEvent.EventType);
    }
}
