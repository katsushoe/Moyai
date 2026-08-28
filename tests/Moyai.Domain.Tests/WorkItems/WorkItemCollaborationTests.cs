using Moyai.Domain.WorkItems;

namespace Moyai.Domain.Tests.WorkItems;

public sealed class WorkItemCollaborationTests
{
    [Fact]
    public void RelationRejectsSelfReferenceAndUnsupportedType()
    {
        Guid projectId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => WorkItemRelation.Create(projectId, itemId, itemId, "blocks", TimeProvider.System));
        Assert.Throws<ArgumentException>(() => WorkItemRelation.Create(projectId, itemId, Guid.NewGuid(), "unknown", TimeProvider.System));
    }

    [Fact]
    public void CommentAndCommitLinkValidateRequiredValues()
    {
        Assert.Throws<ArgumentException>(() => WorkItemComment.Create(Guid.NewGuid(), Guid.NewGuid(), " ", "agent", "codex", TimeProvider.System));
        Assert.Throws<ArgumentException>(() => WorkItemCommitLink.Create(Guid.NewGuid(), Guid.NewGuid(), "abc", "closes", TimeProvider.System));
    }
}
