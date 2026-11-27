using Moyai.Configuration;

namespace Moyai.Cli;

/// <summary>ホストの観測を期限まで続け、期限切れと呼出元のキャンセルを区別します。</summary>
internal static class HostControlWait
{
    internal static async Task<HostControlResponse?> WaitAsync(
        Func<CancellationToken, Task<HostControlResponse?>> probe,
        Func<HostControlResponse?, bool> completed, Action checkProcess,
        TimeSpan budget, string timeoutMessage, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                HostControlResponse? response = await probe(timeout.Token);
                if (completed(response)) return response;
                checkProcess();
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage, exception);
        }
    }
}
