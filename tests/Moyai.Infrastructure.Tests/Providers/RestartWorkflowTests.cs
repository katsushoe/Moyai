using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly int[] AllPhases = [0, 1, 2, 3, 4, 5];
    private static readonly int[] FirstThreePhases = [0, 1, 2];

    [Fact]
    public async Task SuccessExecutesEveryPhaseInOrderOnce()
    {
        var called = new List<int>();
        Func<Task>[] actions = Enumerable.Range(0, 6).Select(i => (Func<Task>)(() => { called.Add(i); return Task.CompletedTask; })).ToArray();
        await RestartWorkflow.RunAsync(new RestartSequence(Now.AddMinutes(1)), actions, () => Now);
        Assert.Equal(AllPhases, called);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task PhaseFailureStopsLaterPhasesWithoutRetry(int failing)
    {
        var called = new List<int>();
        Func<Task>[] actions = Enumerable.Range(0, 6).Select(i => (Func<Task>)(() =>
        {
            called.Add(i);
            return i == failing ? Task.FromException(new IOException("fixture failure")) : Task.CompletedTask;
        })).ToArray();
        await Assert.ThrowsAsync<IOException>(() => RestartWorkflow.RunAsync(new RestartSequence(Now.AddMinutes(1)), actions, () => Now));
        Assert.Equal(Enumerable.Range(0, failing + 1), called);
    }

    [Fact]
    public async Task ExpirationDuringAPhaseStopsBeforeTheNextPhase()
    {
        DateTimeOffset current = Now;
        var called = new List<int>();
        Func<Task>[] actions = Enumerable.Range(0, 6).Select(i => (Func<Task>)(() =>
        {
            called.Add(i);
            if (i == 2) current = Now.AddMinutes(1);
            return Task.CompletedTask;
        })).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RestartWorkflow.RunAsync(new RestartSequence(Now.AddMinutes(1)), actions, () => current));
        Assert.Equal(FirstThreePhases, called);
    }
}
