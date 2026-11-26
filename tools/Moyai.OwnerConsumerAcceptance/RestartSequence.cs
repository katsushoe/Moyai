namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Controls the single attempt and deadline for the isolated restart case.</summary>
internal sealed class RestartSequence(DateTimeOffset expires)
{
    private RestartStep _next = RestartStep.ReadA;
    private bool _inProgress;

    internal void Begin(RestartStep step, DateTimeOffset now)
    {
        if (now >= expires || _inProgress || _next != step || step == RestartStep.Complete)
            throw new InvalidOperationException("Restart acceptance sequence is invalid or expired.");
        _inProgress = true;
    }

    internal void Complete(RestartStep step, DateTimeOffset now)
    {
        if (now >= expires || !_inProgress || _next != step)
            throw new InvalidOperationException("Restart acceptance sequence is invalid or expired.");
        _inProgress = false;
        _next = step switch
        {
            RestartStep.ReadA => RestartStep.ObserveAStopped,
            RestartStep.ObserveAStopped => RestartStep.ObserveBReady,
            RestartStep.ObserveBReady => RestartStep.CheckStaleGeneration,
            RestartStep.CheckStaleGeneration => RestartStep.CheckOldCredential,
            RestartStep.CheckOldCredential => RestartStep.ReadB,
            RestartStep.ReadB => RestartStep.Complete,
            _ => throw new InvalidOperationException("Restart acceptance is already complete.")
        };
    }
}

internal enum RestartStep { ReadA, ObserveAStopped, ObserveBReady, CheckStaleGeneration, CheckOldCredential, ReadB, Complete }
