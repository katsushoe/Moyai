using Moyai.Domain.Builds;

namespace Moyai.Domain.Tests.Builds;

public sealed class BuildTests
{
    [Fact]
    public void BuildLifecycleRecordsCommitAndCompletion()
    {
        Build build = Build.Create(Guid.NewGuid(), "dotnet", "abc123", "Release", null, "agent", "test", TimeProvider.System);

        build.Start(TimeProvider.System);
        build.Succeed(TimeProvider.System);

        Assert.Equal(BuildStatus.Succeeded, build.Status);
        Assert.Equal("abc123", build.SourceCommit);
        Assert.NotNull(build.StartedAt);
        Assert.NotNull(build.FinishedAt);
        Assert.Equal(3, build.Revision);
    }

    [Fact]
    public void SucceedBeforeStartIsRejected()
    {
        Build build = Build.Create(Guid.NewGuid(), "dotnet", "abc123", "Release", null, "agent", "test", TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => build.Succeed(TimeProvider.System));
    }
}
