using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartSequenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteSequenceAllowsEachStepOnlyOnce()
    {
        var sequence = new RestartSequence(Now.AddMinutes(10));
        foreach (RestartStep step in new[] { RestartStep.ReadA, RestartStep.ObserveAStopped, RestartStep.ObserveBReady,
            RestartStep.CheckStaleGeneration, RestartStep.CheckOldCredential, RestartStep.ReadB })
        {
            sequence.Begin(step, Now);
            sequence.Complete(step, Now);
        }
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(RestartStep.ReadB, Now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LaterStepCannotStartBeforeARead(int step)
    {
        var sequence = new RestartSequence(Now.AddMinutes(10));
        Assert.Throws<InvalidOperationException>(() => sequence.Begin((RestartStep)step, Now));
    }

    [Fact]
    public void FailedInFlightStepCannotBeReplayedOrSkipped()
    {
        var sequence = new RestartSequence(Now.AddMinutes(10));
        sequence.Begin(RestartStep.ReadA, Now);
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(RestartStep.ReadA, Now));
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(RestartStep.ObserveAStopped, Now));
    }

    [Fact]
    public void ExpirationDuringOperationPreventsCompletionAndNextStep()
    {
        var expiry = Now.AddSeconds(1);
        var sequence = new RestartSequence(expiry);
        sequence.Begin(RestartStep.ReadA, Now);
        Assert.Throws<InvalidOperationException>(() => sequence.Complete(RestartStep.ReadA, expiry));
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(RestartStep.ObserveAStopped, expiry));
    }
}
