using Moyai.Domain.Deployments;

namespace Moyai.Domain.Tests.Deployments;

public sealed class DeploymentTests
{
    [Fact]
    public void TargetRejectsServerWithoutKelpieTarget() => Assert.Throws<ArgumentNullException>(() => DeploymentTarget.Create(Guid.NewGuid(), "production", "server", "/app", null, null, TimeProvider.System));

    [Fact]
    public void DeploymentTracksBuildAndSourceCommit()
    {
        DeploymentTarget target = DeploymentTarget.Create(Guid.NewGuid(), "local", "local", "install", null, null, TimeProvider.System);
        Deployment deployment = Deployment.Create(target.ProjectId, target, Guid.NewGuid(), null, "abc123", null, null, "agent", "test", TimeProvider.System);

        deployment.Transition(DeploymentStatus.Preparing, TimeProvider.System);
        deployment.Transition(DeploymentStatus.Succeeded, TimeProvider.System);

        Assert.Equal("abc123", deployment.SourceCommit);
        Assert.Equal(DeploymentStatus.Succeeded, deployment.Status);
        Assert.NotNull(deployment.FinishedAt);
    }
}
