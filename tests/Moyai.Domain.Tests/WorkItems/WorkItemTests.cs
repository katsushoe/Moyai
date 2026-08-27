using Moyai.Domain.WorkItems;

namespace Moyai.Domain.Tests.WorkItems;

public sealed class WorkItemTests
{
    [Theory]
    [InlineData(WorkItemType.Issue, "ISSUE-1", "open")]
    [InlineData(WorkItemType.Bug, "BUG-1", "reported")]
    [InlineData(WorkItemType.ChangeRequest, "CR-1", "proposed")]
    [InlineData(WorkItemType.Feature, "FEAT-1", "proposed")]
    [InlineData(WorkItemType.Risk, "RISK-1", "identified")]
    [InlineData(WorkItemType.Decision, "DEC-1", "proposed")]
    public void CreateAssignsKeyAndInitialStatus(WorkItemType type, string expectedKey, string expectedStatus)
    {
        WorkItem item = WorkItem.Create(Guid.NewGuid(), type, 1, "Title", "agent", "codex", TimeProvider.System);
        Assert.Equal(expectedKey, item.Key);
        Assert.Equal(expectedStatus, item.Status);
        Assert.Equal(WorkItemPriority.Normal, item.Priority);
        Assert.Equal(1, item.Revision);
    }

    [Fact]
    public void TransitionToAllowedTransitionUpdatesStatusAndRevision()
    {
        WorkItem item = WorkItem.Create(Guid.NewGuid(), WorkItemType.Bug, 42, "Bug", "agent", "codex", TimeProvider.System);
        item.TransitionTo("confirmed", TimeProvider.System);
        Assert.Equal("confirmed", item.Status);
        Assert.Equal(2, item.Revision);
    }

    [Fact]
    public void TransitionToInvalidTransitionThrows()
    {
        WorkItem item = WorkItem.Create(Guid.NewGuid(), WorkItemType.Bug, 1, "Bug", "agent", "codex", TimeProvider.System);
        Assert.Throws<InvalidWorkItemTransitionException>(() => item.TransitionTo("closed", TimeProvider.System));
    }

    [Fact]
    public void UpdateDeleteAndRestoreIncrementRevision()
    {
        WorkItem item = WorkItem.Create(Guid.NewGuid(), WorkItemType.Bug, 1, "Bug", "agent", "codex", TimeProvider.System);
        item.Update("Updated", "Description", WorkItemPriority.High, WorkItemSeverity.Major, "owner", "{}", TimeProvider.System);
        item.Delete(TimeProvider.System);
        Assert.NotNull(item.DeletedAt);
        item.Restore(TimeProvider.System);
        Assert.Null(item.DeletedAt);
        Assert.Equal(4, item.Revision);
    }
}
