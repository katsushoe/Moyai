namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Executes each accepted phase once, stopping on any error or expired window.</summary>
internal static class RestartWorkflow
{
    internal static async Task RunAsync(RestartSequence sequence, IReadOnlyList<Func<Task>> actions, Func<DateTimeOffset> clock)
    {
        RestartStep[] steps = [RestartStep.ReadA, RestartStep.ObserveAStopped, RestartStep.ObserveBReady,
            RestartStep.CheckStaleGeneration, RestartStep.CheckOldCredential, RestartStep.ReadB];
        if (actions.Count != steps.Length) throw new InvalidOperationException("Invalid restart workflow.");
        for (int i = 0; i < steps.Length; i++)
        {
            sequence.Begin(steps[i], clock());
            await actions[i]().ConfigureAwait(false);
            sequence.Complete(steps[i], clock());
        }
    }
}
