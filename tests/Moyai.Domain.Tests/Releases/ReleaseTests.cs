using Moyai.Domain.Releases;

namespace Moyai.Domain.Tests.Releases;

public sealed class ReleaseTests
{
    [Fact]
    public void CreateWithValidInputCreatesDraftRelease()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));

        Release release = Release.Create(Guid.NewGuid(), "1.0.0", ReleaseChannel.Stable, "notes", time);

        Assert.Equal(ReleaseStatus.Draft, release.Status);
        Assert.Equal(1, release.Revision);
        Assert.Equal("notes", release.ReleaseNotes);
    }

    [Fact]
    public void TransitionToFollowingPublishWorkflowReachesReleased()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        Release release = Release.Create(Guid.NewGuid(), "1.0.0", ReleaseChannel.Stable, null, time);

        release.TransitionTo(ReleaseStatus.Planned, time);
        release.TransitionTo(ReleaseStatus.Preparing, time);
        release.TransitionTo(ReleaseStatus.Ready, time);
        release.TransitionTo(ReleaseStatus.Publishing, time);
        release.TransitionTo(ReleaseStatus.Released, time);

        Assert.Equal(ReleaseStatus.Released, release.Status);
        Assert.NotNull(release.ReleasedAt);
        Assert.Equal(6, release.Revision);
    }

    [Theory]
    [InlineData(ReleaseStatus.Preparing)]
    [InlineData(ReleaseStatus.Ready)]
    public void TransitionToRecoveringFromFailureAllowsDocumentedTarget(ReleaseStatus target)
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        Release release = Release.Create(Guid.NewGuid(), "1.0.0", ReleaseChannel.Stable, null, time);
        release.TransitionTo(ReleaseStatus.Planned, time);
        release.TransitionTo(ReleaseStatus.Preparing, time);
        release.TransitionTo(ReleaseStatus.Ready, time);
        release.TransitionTo(ReleaseStatus.Publishing, time);
        release.TransitionTo(ReleaseStatus.Failed, time);

        release.TransitionTo(target, time);

        Assert.Equal(target, release.Status);
    }

    [Fact]
    public void TransitionToNotAllowedThrows()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        Release release = Release.Create(Guid.NewGuid(), "1.0.0", ReleaseChannel.Stable, null, time);

        Assert.Throws<InvalidReleaseTransitionException>(() => release.TransitionTo(ReleaseStatus.Released, time));
    }

    [Fact]
    public void TransitionToFailedAllowsFalsePositiveReconciliation()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        Release release = Release.Create(Guid.NewGuid(), "1.0.0", ReleaseChannel.Stable, null, time);
        release.TransitionTo(ReleaseStatus.Planned, time);
        release.TransitionTo(ReleaseStatus.Preparing, time);
        release.TransitionTo(ReleaseStatus.Ready, time);
        release.TransitionTo(ReleaseStatus.Publishing, time);
        release.TransitionTo(ReleaseStatus.Released, time);

        release.TransitionTo(ReleaseStatus.Failed, time);

        Assert.Equal(ReleaseStatus.Failed, release.Status);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
