using Moyai.Cli;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class HostControlWaitTests
{
    [Fact]
    public async Task RestartWaitRejectsStillRunningProcessWithoutTerminatingIt()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => ServiceCommands.WaitForStoppedProcessAsync(
            Environment.ProcessId, TimeSpan.FromMilliseconds(50)));
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        Assert.False(current.HasExited);
    }

    [Fact]
    public async Task RestartWaitAcceptsAlreadyExitedProcess()
        => await ServiceCommands.WaitForStoppedProcessAsync(int.MaxValue, TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task DelayedReadinessContinuesAfterAbsentProbes()
    {
        int probes = 0;
        HostControlResponse expected = new(true, "running", 123, "http://127.0.0.1:12345");
        HostControlResponse? actual = await HostControlWait.WaitAsync(
            _ => Task.FromResult(++probes < 3 ? null : expected), response => response is { Ok: true },
            () => { }, TimeSpan.FromSeconds(60), "startup deadline");
        Assert.Same(expected, actual);
        Assert.Equal(3, probes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeadlineDuringProbeOrDelayProducesTimeout(bool blockProbe)
    {
        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => HostControlWait.WaitAsync(
            async token =>
            {
                if (blockProbe) await Task.Delay(Timeout.Infinite, token);
                return null;
            }, _ => false, () => { }, TimeSpan.FromMilliseconds(50), "startup deadline"));
        Assert.Equal("startup deadline", error.Message);
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeHostTimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostControlWait.WaitAsync(
            _ => Task.FromResult<HostControlResponse?>(null), _ => false, () => { },
            TimeSpan.FromSeconds(1), "deadline", cancelled.Token));
    }

    [Fact]
    public async Task ProcessExitStopsPollingBeforeReadiness()
    {
        int probes = 0;
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => HostControlWait.WaitAsync(
            _ => { probes++; return Task.FromResult<HostControlResponse?>(null); }, _ => false,
            () => throw new InvalidOperationException("host exited"), TimeSpan.FromSeconds(1), "deadline"));
        Assert.Equal("host exited", error.Message);
        Assert.Equal(1, probes);
    }
}
